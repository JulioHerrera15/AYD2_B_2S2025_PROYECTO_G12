using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Nucleo de HU-001. Reserva el inventario de una orden con control de concurrencia
/// (ADR-005) y marca de expiracion por TTL, y encola el evento de resultado en el
/// outbox transversal.
///
/// No llama a <c>SaveChangesAsync</c>: la transaccion la cierra quien llama, para
/// que existencias + reserva + fila de outbox (y, en el consumo, la marca de
/// mensaje procesado) queden en una sola transaccion. Lo usan el consumidor de
/// <c>ordenes.reserva.solicitada</c> (EventConsumerBase guarda) y el endpoint REST
/// (<see cref="IUnidadDeTrabajo" /> guarda y traduce el choque de version a
/// <see cref="ExcepcionConflictoConcurrencia" />).
///
/// Idempotencia: si la orden ya tiene reserva, se devuelve esa sin tocar nada ni
/// re-emitir.
/// </summary>
public sealed class ServicioReservas
{
    private readonly IRepositorioExistencias _existencias;
    private readonly IRepositorioReservas _reservas;
    private readonly IOutboxWriter _outbox;
    private readonly IGeneradorCorrelativoReserva _generadorCorrelativo;
    private readonly InventarioOptions _opciones;

    public ServicioReservas(
        IRepositorioExistencias existencias,
        IRepositorioReservas reservas,
        IOutboxWriter outbox,
        IGeneradorCorrelativoReserva generadorCorrelativo,
        IOptions<InventarioOptions> opciones)
    {
        _existencias = existencias;
        _reservas = reservas;
        _outbox = outbox;
        _generadorCorrelativo = generadorCorrelativo;
        _opciones = opciones.Value;
    }

    public async Task<ResultadoReservaDto> ReservarAsync(SolicitudReserva solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (string.IsNullOrWhiteSpace(solicitud.OrdenId))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(solicitud));
        }

        var pedidoPorSku = ConsolidarLineas(solicitud.Lineas);

        // Idempotencia: una orden reserva una sola vez. Reintentos del mensaje o
        // llamadas repetidas al endpoint devuelven la reserva ya hecha.
        var existente = await _reservas.ObtenerPorOrdenAsync(solicitud.OrdenId, cancelacion);
        if (existente is not null)
        {
            return ResultadoReservaDto.YaReservada(ReservaDto.Desde(existente));
        }

        var ttlEfectivo = _opciones.TtlEfectivo(solicitud.TtlReservaSegundos);
        var skus = pedidoPorSku.Keys.ToArray();
        var existencias = await _existencias.ListarPorSkusAsync(skus, cancelacion);

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            pedidoPorSku.Select(par => new LineaReservada(par.Key, par.Value)).ToList(),
            existencias,
            solicitud.UbicacionPreferidaId,
            solicitud.MetodoEntrega);

        if (seleccion.UbicacionId is null)
        {
            return Rechazar(solicitud.OrdenId, seleccion);
        }

        var existenciasElegidas = existencias
            .Where(existencia => existencia.UbicacionId == seleccion.UbicacionId)
            .ToDictionary(existencia => existencia.Sku);

        foreach (var (sku, cantidad) in pedidoPorSku)
        {
            existenciasElegidas[sku].Reservar(cantidad);
        }

        var tipoUbicacion = existenciasElegidas.Values.First().TipoUbicacion;
        var correlativo = await _generadorCorrelativo.SiguienteAsync(cancelacion);
        var reservaId = $"RSV-{DateTime.UtcNow:yyyy}-{correlativo:D6}";
        var creadaEn = DateTime.UtcNow;

        var reserva = Reserva.Crear(
            reservaId,
            solicitud.OrdenId,
            seleccion.UbicacionId,
            tipoUbicacion,
            ttlEfectivo,
            pedidoPorSku.Select(par => (par.Key, par.Value)),
            creadaEn);

        await _reservas.AgregarAsync(reserva, cancelacion);

        _outbox.Encolar(
            InventarioReservado.EventType,
            InventarioReservado.AggregateType,
            reserva.Id,
            reserva.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            InventarioReservado.RoutingKey,
            new InventarioReservadoData(
                ReservaId: reserva.Id,
                OrdenId: reserva.OrdenId,
                UbicacionId: reserva.UbicacionId,
                TipoUbicacion: reserva.TipoUbicacion,
                TtlSegundosAplicado: reserva.TtlSegundos,
                Lineas: reserva.Lineas
                    .Select(linea => new ReservaConfirmadaLineaData(linea.Sku, linea.Cantidad))
                    .ToList(),
                ReservadaEn: reserva.CreadaEn,
                ExpiraEn: reserva.ExpiraEn));

        return ResultadoReservaDto.Confirmada(ReservaDto.Desde(reserva));
    }

    public async Task<ReservaDto> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion)
    {
        var reserva = await _reservas.ObtenerPorOrdenAsync(ordenId, cancelacion)
            ?? throw new ExcepcionReservaNoEncontrada(ordenId);

        return ReservaDto.Desde(reserva);
    }

    /// <summary>
    /// Reservas de una ubicacion para la vista unificada de HU-016. Con
    /// <paramref name="estado" /> en null trae todo el historial de la ubicacion; el
    /// caso de uso habitual del operador es filtrar por ACTIVA, que es lo que hoy
    /// tiene comprometido esa tienda o centro de distribucion.
    /// </summary>
    public async Task<IReadOnlyList<ReservaDto>> ListarPorUbicacionAsync(
        string ubicacionId,
        EstadoReserva? estado,
        CancellationToken cancelacion)
    {
        var reservas = await _reservas.ListarPorUbicacionAsync(ubicacionId, estado, cancelacion);
        return reservas.Select(ReservaDto.Desde).ToList();
    }

    private ResultadoReservaDto Rechazar(string ordenId, SeleccionUbicacion seleccion)
    {
        var motivo = seleccion.Motivo ?? MotivoRechazoReserva.STOCK_INSUFICIENTE;

        var faltantes = seleccion.Faltantes
            .Select(faltante => new LineaFaltanteDto(faltante.Sku, faltante.Solicitado, faltante.Disponible))
            .ToList();

        // La reserva rechazada no crea agregado: el evento viaja bajo el ordenId y
        // conserva la correlacion por el transactionId del mensaje entrante. sequence
        // 1 porque es el unico evento de esta "no-reserva".
        _outbox.Encolar(
            ReservaInventarioRechazada.EventType,
            ReservaInventarioRechazada.AggregateType,
            ordenId,
            1,
            TopologiaMensajeria.ExchangeEventos,
            ReservaInventarioRechazada.RoutingKey,
            new ReservaInventarioRechazadaData(
                OrdenId: ordenId,
                Motivo: motivo,
                Lineas: seleccion.Faltantes
                    .Select(faltante => new ReservaRechazadaLineaData(
                        faltante.Sku, faltante.Solicitado, faltante.Disponible))
                    .ToList(),
                RechazadaEn: DateTime.UtcNow));

        return ResultadoReservaDto.RechazadaPorStock(motivo, faltantes);
    }

    private static IReadOnlyDictionary<string, int> ConsolidarLineas(IReadOnlyList<LineaSolicitudReserva>? lineas)
    {
        if (lineas is null || lineas.Count == 0)
        {
            throw new ArgumentException("La solicitud de reserva requiere al menos una linea.", nameof(lineas));
        }

        var consolidado = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var linea in lineas)
        {
            if (string.IsNullOrWhiteSpace(linea.Sku))
            {
                throw new ArgumentException("Toda linea debe traer un SKU.", nameof(lineas));
            }

            if (linea.Cantidad <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineas), "La cantidad de cada linea debe ser mayor que cero.");
            }

            consolidado[linea.Sku] = consolidado.GetValueOrDefault(linea.Sku) + linea.Cantidad;
        }

        return consolidado;
    }
}
