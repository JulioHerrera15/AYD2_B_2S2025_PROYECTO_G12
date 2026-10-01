using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Retencion de stock mientras el producto esta en el carrito (agregado
/// <see cref="ReservaCarrito" />). Lo llama Auth por la red interna cada vez que el
/// cliente agrega, cambia o quita un producto, y al asociar un carrito de invitado.
///
/// Las unidades retenidas suman a <see cref="Existencia.CantidadReservada" />, asi
/// que el disponible que ven los demas clientes ya las descuenta. Cada operacion
/// ajusta la existencia por la diferencia (delta) y renueva el TTL de todas las
/// lineas activas del carrito.
///
/// Donde se retiene: (1) en la sucursal preferida si alcanza; (2) donde ya estaba
/// la linea, si alcanza; (3) en la tienda mas cercana a la preferida con stock
/// (<see cref="SelectorSucursalCercana" />); (4) donde diga
/// <see cref="SelectorUbicacionReserva" />. Si nada alcanza, se lanza
/// <see cref="ExcepcionSinStockCarrito" /> sin haber tocado nada.
///
/// No llama a SaveChanges: lo hace el endpoint, igual que con la reserva de una orden.
/// </summary>
public sealed class ServicioReservasCarrito
{
    private readonly IRepositorioReservasCarrito _retenciones;
    private readonly IRepositorioExistencias _existencias;
    private readonly IRepositorioSucursales _sucursales;
    private readonly InventarioOptions _opciones;
    private readonly ILogger<ServicioReservasCarrito> _logger;

    public ServicioReservasCarrito(
        IRepositorioReservasCarrito retenciones,
        IRepositorioExistencias existencias,
        IRepositorioSucursales sucursales,
        IOptions<InventarioOptions> opciones,
        ILogger<ServicioReservasCarrito> logger)
    {
        _retenciones = retenciones;
        _existencias = existencias;
        _sucursales = sucursales;
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <summary>
    /// Deja retenidas <paramref name="cantidad" /> unidades del SKU para el carrito
    /// (cantidad total, no incremento). Crea la linea, la ajusta o la reactiva si
    /// estaba EXPIRADA; la existencia solo se mueve por la diferencia.
    /// </summary>
    public async Task<LineaReservaCarritoDto> RetenerAsync(
        string carritoId,
        string sku,
        int cantidad,
        string? ubicacionPreferidaId,
        CancellationToken cancelacion)
    {
        ValidarCarrito(carritoId);
        ValidarSku(sku);

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad a retener debe ser mayor que cero.");
        }

        var ahora = DateTime.UtcNow;
        var ttl = _opciones.CarritoTtlEfectivo;

        var lineas = await _retenciones.ListarVigentesPorCarritoAsync(carritoId, cancelacion);
        var linea = lineas.FirstOrDefault(retencion => retencion.Sku == sku);
        var preferida = Normalizar(ubicacionPreferidaId) ?? linea?.UbicacionPreferidaId;

        var existencias = await _existencias.ListarPorSkusAsync(new[] { sku }, cancelacion);
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);

        // Lo que la propia linea ya retiene cuenta como disponible para ella: subir de
        // 2 a 3 unidades solo necesita 1 mas, no 3.
        var propias = UnidadesRetenidas(linea);
        var ubicacionActual = linea is { EstaActiva: true } ? linea.UbicacionId : null;

        var destino = ElegirUbicacion(sku, cantidad, preferida, ubicacionActual, existencias, sucursales, propias);
        if (destino is null)
        {
            throw SinStock(sku, cantidad, preferida, existencias, sucursales, propias);
        }

        if (linea is { EstaActiva: true })
        {
            Devolver(linea, existencias);
        }

        destino.Reservar(cantidad);

        if (linea is null)
        {
            linea = ReservaCarrito.Crear(
                carritoId,
                sku,
                destino.UbicacionId,
                destino.TipoUbicacion,
                cantidad,
                preferida,
                ttl,
                ahora);

            await _retenciones.AgregarAsync(linea, cancelacion);
        }
        else
        {
            linea.Reasignar(destino.UbicacionId, destino.TipoUbicacion, cantidad, preferida, ttl, ahora);
        }

        RenovarResto(lineas, linea, ttl, ahora);

        _logger.LogInformation(
            "Carrito {CarritoId}: {Cantidad} unidad(es) de {Sku} retenidas en {UbicacionId} hasta {ExpiraEn}.",
            carritoId,
            cantidad,
            sku,
            linea.UbicacionId,
            linea.ExpiraEn);

        return LineaReservaCarritoDto.Desde(linea, PorId(sucursales));
    }

    /// <summary>Libera la linea del SKU (vuelve al disponible si estaba ACTIVA). Devuelve false si no habia linea.</summary>
    public async Task<bool> LiberarItemAsync(string carritoId, string sku, CancellationToken cancelacion)
    {
        ValidarCarrito(carritoId);
        ValidarSku(sku);

        var lineas = await _retenciones.ListarVigentesPorCarritoAsync(carritoId, cancelacion);
        var linea = lineas.FirstOrDefault(retencion => retencion.Sku == sku);
        if (linea is null)
        {
            return false;
        }

        if (linea.EstaActiva)
        {
            var existencias = await _existencias.ListarPorSkusAsync(new[] { sku }, cancelacion);
            Devolver(linea, existencias);
        }

        linea.Liberar();
        RenovarResto(lineas, linea, _opciones.CarritoTtlEfectivo, DateTime.UtcNow);

        return true;
    }

    /// <summary>Libera todas las lineas del carrito. Devuelve cuantas cerro.</summary>
    public async Task<int> LiberarCarritoAsync(string carritoId, CancellationToken cancelacion)
    {
        ValidarCarrito(carritoId);

        var lineas = await _retenciones.ListarVigentesPorCarritoAsync(carritoId, cancelacion);
        if (lineas.Count == 0)
        {
            return 0;
        }

        var existencias = await _existencias.ListarPorSkusAsync(
            lineas.Select(linea => linea.Sku).Distinct().ToArray(),
            cancelacion);

        foreach (var linea in lineas)
        {
            if (linea.EstaActiva)
            {
                Devolver(linea, existencias);
            }

            linea.Liberar();
        }

        return lineas.Count;
    }

    /// <summary>
    /// Extiende el TTL de las lineas ACTIVA y reactiva las EXPIRADA donde estaban si
    /// todavia hay stock ahi. Una EXPIRADA que ya no cabe se devuelve tal cual, con la
    /// tienda mas cercana que si tiene como sugerencia. Si se indica
    /// <paramref name="skus" />, las lineas de otros SKU se cierran.
    /// </summary>
    public async Task<ReservaCarritoDto> RenovarAsync(
        string carritoId,
        IReadOnlyCollection<string>? skus,
        CancellationToken cancelacion)
    {
        ValidarCarrito(carritoId);

        var ahora = DateTime.UtcNow;
        var ttl = _opciones.CarritoTtlEfectivo;

        var lineas = await _retenciones.ListarVigentesPorCarritoAsync(carritoId, cancelacion);
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);

        if (lineas.Count == 0)
        {
            return Construir(carritoId, lineas, sucursales);
        }

        var existencias = await _existencias.ListarPorSkusAsync(
            lineas.Select(linea => linea.Sku).Distinct().ToArray(),
            cancelacion);

        var sugeridas = new Dictionary<Guid, SucursalCercanaDto?>();

        foreach (var linea in lineas)
        {
            if (skus is not null && !skus.Contains(linea.Sku))
            {
                if (linea.EstaActiva)
                {
                    Devolver(linea, existencias);
                }

                linea.Liberar();
                continue;
            }

            if (linea.EstaActiva)
            {
                linea.Renovar(ttl, ahora);
                continue;
            }

            var existencia = Buscar(existencias, linea.Sku, linea.UbicacionId);
            if (existencia is not null && existencia.Disponible >= linea.Cantidad)
            {
                existencia.Reservar(linea.Cantidad);
                linea.Reasignar(
                    linea.UbicacionId,
                    linea.TipoUbicacion,
                    linea.Cantidad,
                    linea.UbicacionPreferidaId,
                    ttl,
                    ahora);
                continue;
            }

            var cercana = SelectorSucursalCercana.Elegir(
                new[] { new LineaReservada(linea.Sku, linea.Cantidad) },
                existencias,
                sucursales,
                linea.UbicacionPreferidaId ?? linea.UbicacionId);

            sugeridas[linea.Id] = cercana is null
                ? null
                : SucursalCercanaDto.Crear(linea.Sku, linea.UbicacionPreferidaId ?? linea.UbicacionId, cercana);
        }

        return Construir(carritoId, lineas.Where(linea => linea.EstaVigente), sucursales, sugeridas);
    }

    /// <summary>
    /// Mueve las lineas de un carrito a otro (invitado que inicia sesion). Si el
    /// destino ya tiene el mismo SKU, se fusionan sumando cantidades, igual que hace
    /// Auth con el carrito en Redis; si la suma no cabe en ninguna ubicacion, el
    /// destino conserva lo suyo y las unidades del origen vuelven al disponible.
    /// </summary>
    public async Task<ReservaCarritoDto> TransferirAsync(
        string carritoOrigenId,
        string carritoDestinoId,
        CancellationToken cancelacion)
    {
        ValidarCarrito(carritoOrigenId);
        ValidarCarrito(carritoDestinoId);

        if (string.Equals(carritoOrigenId, carritoDestinoId, StringComparison.Ordinal))
        {
            return await ObtenerAsync(carritoDestinoId, cancelacion);
        }

        var ahora = DateTime.UtcNow;
        var ttl = _opciones.CarritoTtlEfectivo;

        var lineasOrigen = await _retenciones.ListarVigentesPorCarritoAsync(carritoOrigenId, cancelacion);
        var lineasDestino = (await _retenciones.ListarVigentesPorCarritoAsync(carritoDestinoId, cancelacion)).ToList();
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);

        if (lineasOrigen.Count == 0)
        {
            return Construir(carritoDestinoId, lineasDestino, sucursales);
        }

        var existencias = await _existencias.ListarPorSkusAsync(
            lineasOrigen.Concat(lineasDestino).Select(linea => linea.Sku).Distinct().ToArray(),
            cancelacion);

        foreach (var origen in lineasOrigen)
        {
            var destino = lineasDestino.FirstOrDefault(linea => linea.Sku == origen.Sku);

            if (destino is null)
            {
                origen.TransferirA(carritoDestinoId);
                lineasDestino.Add(origen);
                continue;
            }

            Fusionar(origen, destino, existencias, sucursales, ttl, ahora);
        }

        foreach (var linea in lineasDestino)
        {
            linea.Renovar(ttl, ahora);
        }

        _logger.LogInformation(
            "Carrito {Origen} transferido a {Destino}: {Lineas} linea(s).",
            carritoOrigenId,
            carritoDestinoId,
            lineasOrigen.Count);

        return Construir(carritoDestinoId, lineasDestino.Where(linea => linea.EstaVigente), sucursales);
    }

    /// <summary>Lineas vigentes del carrito con su estado. No modifica nada.</summary>
    public async Task<ReservaCarritoDto> ObtenerAsync(string carritoId, CancellationToken cancelacion)
    {
        ValidarCarrito(carritoId);

        var lineas = await _retenciones.ListarVigentesPorCarritoAsync(carritoId, cancelacion);
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);

        return Construir(carritoId, lineas, sucursales);
    }

    private void Fusionar(
        ReservaCarrito origen,
        ReservaCarrito destino,
        IReadOnlyList<Existencia> existencias,
        IReadOnlyList<Sucursal> sucursales,
        int ttl,
        DateTime ahora)
    {
        var total = origen.Cantidad + destino.Cantidad;
        var preferida = destino.UbicacionPreferidaId ?? origen.UbicacionPreferidaId;
        var actual = destino.EstaActiva ? destino.UbicacionId : origen.EstaActiva ? origen.UbicacionId : null;
        var propias = UnidadesRetenidas(origen, destino);

        var ubicacion = ElegirUbicacion(origen.Sku, total, preferida, actual, existencias, sucursales, propias);

        if (origen.EstaActiva)
        {
            Devolver(origen, existencias);
        }

        origen.Liberar();

        if (ubicacion is null)
        {
            _logger.LogWarning(
                "Al fusionar carritos no cupieron {Total} unidades de {Sku}; el destino conserva {Cantidad}.",
                total,
                destino.Sku,
                destino.Cantidad);

            return;
        }

        if (destino.EstaActiva)
        {
            Devolver(destino, existencias);
        }

        ubicacion.Reservar(total);
        destino.Reasignar(ubicacion.UbicacionId, ubicacion.TipoUbicacion, total, preferida, ttl, ahora);
    }

    /// <summary>
    /// Ubicacion donde retener, segun el orden documentado en la clase, contando como
    /// disponibles las unidades que <paramref name="propias" /> ya retiene.
    /// </summary>
    private static Existencia? ElegirUbicacion(
        string sku,
        int cantidad,
        string? preferida,
        string? actual,
        IReadOnlyList<Existencia> existencias,
        IReadOnlyList<Sucursal> sucursales,
        IReadOnlyDictionary<(string UbicacionId, string Sku), int> propias)
    {
        bool Alcanza(Existencia? existencia) =>
            existencia is not null && SelectorUbicacionReserva.DisponibleEfectivo(existencia, propias) >= cantidad;

        var enPreferida = preferida is null ? null : Buscar(existencias, sku, preferida);
        if (Alcanza(enPreferida))
        {
            return enPreferida;
        }

        var enActual = actual is null ? null : Buscar(existencias, sku, actual);
        if (Alcanza(enActual))
        {
            return enActual;
        }

        var lineas = new[] { new LineaReservada(sku, cantidad) };

        if (preferida is not null)
        {
            var cercana = SelectorSucursalCercana.Elegir(lineas, existencias, sucursales, preferida, propias);
            if (cercana is not null)
            {
                return Buscar(existencias, sku, cercana.Sucursal.Id);
            }
        }

        // Sin preferida, o ninguna tienda alcanza: el selector general, que tambien
        // considera los centros de distribucion.
        var seleccion = SelectorUbicacionReserva.Seleccionar(lineas, existencias, null, MetodoEntrega.DOMICILIO, propias);
        return seleccion.UbicacionId is null ? null : Buscar(existencias, sku, seleccion.UbicacionId);
    }

    private static ExcepcionSinStockCarrito SinStock(
        string sku,
        int cantidad,
        string? preferida,
        IReadOnlyList<Existencia> existencias,
        IReadOnlyList<Sucursal> sucursales,
        IReadOnlyDictionary<(string UbicacionId, string Sku), int> propias)
    {
        var maximo = existencias
            .Where(existencia => existencia.Sku == sku)
            .Select(existencia => SelectorUbicacionReserva.DisponibleEfectivo(existencia, propias))
            .DefaultIfEmpty(0)
            .Max();

        var disponible = Math.Max(0, maximo);

        SucursalCercanaDto? sugerida = null;
        if (disponible > 0)
        {
            var cercana = SelectorSucursalCercana.Elegir(
                new[] { new LineaReservada(sku, 1) },
                existencias,
                sucursales,
                preferida,
                propias);

            sugerida = cercana is null ? null : SucursalCercanaDto.Crear(sku, preferida, cercana);
        }

        return new ExcepcionSinStockCarrito(sku, cantidad, disponible, sugerida);
    }

    private void Devolver(ReservaCarrito linea, IReadOnlyList<Existencia> existencias)
    {
        var existencia = Buscar(existencias, linea.Sku, linea.UbicacionId);
        if (existencia is null)
        {
            // Mismo criterio que la expiracion de reservas: la linea se cierra igual,
            // no puede quedar reteniendo para siempre, pero queda constancia.
            _logger.LogWarning(
                "La retencion {RetencionId} referencia {Sku} en {UbicacionId}, que ya no tiene existencia registrada.",
                linea.Id,
                linea.Sku,
                linea.UbicacionId);

            return;
        }

        existencia.LiberarReserva(linea.Cantidad);
    }

    private static void RenovarResto(IEnumerable<ReservaCarrito> lineas, ReservaCarrito tocada, int ttl, DateTime ahora)
    {
        foreach (var linea in lineas)
        {
            if (!ReferenceEquals(linea, tocada))
            {
                linea.Renovar(ttl, ahora);
            }
        }
    }

    private static IReadOnlyDictionary<(string UbicacionId, string Sku), int> UnidadesRetenidas(
        params ReservaCarrito?[] lineas) =>
        lineas
            .Where(linea => linea is { EstaActiva: true })
            .GroupBy(linea => (linea!.UbicacionId, linea.Sku))
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(linea => linea!.Cantidad));

    private static Existencia? Buscar(IEnumerable<Existencia> existencias, string sku, string ubicacionId) =>
        existencias.FirstOrDefault(existencia => existencia.Sku == sku && existencia.UbicacionId == ubicacionId);

    private static IReadOnlyDictionary<string, Sucursal> PorId(IEnumerable<Sucursal> sucursales) =>
        sucursales.GroupBy(sucursal => sucursal.Id).ToDictionary(grupo => grupo.Key, grupo => grupo.First());

    private static ReservaCarritoDto Construir(
        string carritoId,
        IEnumerable<ReservaCarrito> lineas,
        IReadOnlyList<Sucursal> sucursales,
        IReadOnlyDictionary<Guid, SucursalCercanaDto?>? sugeridas = null)
    {
        var porId = PorId(sucursales);
        var materializadas = lineas.OrderBy(linea => linea.Sku, StringComparer.Ordinal).ToList();

        var expiraEn = materializadas
            .Where(linea => linea.EstaActiva)
            .Select(linea => (DateTime?)linea.ExpiraEn)
            .Min();

        return new ReservaCarritoDto(
            carritoId,
            expiraEn,
            materializadas
                .Select(linea => LineaReservaCarritoDto.Desde(
                    linea,
                    porId,
                    sugeridas is not null && sugeridas.TryGetValue(linea.Id, out var sugerida) ? sugerida : null))
                .ToList());
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static void ValidarCarrito(string carritoId)
    {
        if (string.IsNullOrWhiteSpace(carritoId))
        {
            throw new ArgumentException("El identificador del carrito es obligatorio.", nameof(carritoId));
        }

        if (carritoId.Length > ReservaCarrito.LargoMaximoCarritoId)
        {
            throw new ArgumentException(
                $"El identificador del carrito no puede exceder {ReservaCarrito.LargoMaximoCarritoId} caracteres.",
                nameof(carritoId));
        }
    }

    private static void ValidarSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }
    }
}
