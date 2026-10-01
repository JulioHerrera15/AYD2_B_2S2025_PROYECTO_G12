using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Responde "desde donde saldria este pedido" sin reservar nada: corre el mismo
/// <see cref="SelectorUbicacionReserva" /> que la reserva real, contando como
/// disponibles las retenciones activas del carrito del cliente. No modifica
/// existencias ni retenciones y no se confirma ninguna transaccion.
///
/// Lo usa el checkout para avisar antes de pagar que un envio a domicilio saldra de
/// una tienda (y no del centro de distribucion), o que la tienda de recogida no
/// tiene todo el pedido.
/// </summary>
public sealed class ServicioSimulacionReserva
{
    private readonly IRepositorioExistencias _existencias;
    private readonly IRepositorioReservasCarrito _retencionesCarrito;
    private readonly IRepositorioSucursales _sucursales;

    public ServicioSimulacionReserva(
        IRepositorioExistencias existencias,
        IRepositorioReservasCarrito retencionesCarrito,
        IRepositorioSucursales sucursales)
    {
        _existencias = existencias;
        _retencionesCarrito = retencionesCarrito;
        _sucursales = sucursales;
    }

    public async Task<SimulacionReservaDto> SimularAsync(SimularReservaRequest solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var lineas = Consolidar(solicitud.Lineas);
        var preferida = Normalizar(solicitud.UbicacionPreferidaId);
        var referencia = Normalizar(solicitud.UbicacionReferenciaId);

        var retenciones = string.IsNullOrWhiteSpace(solicitud.CarritoId)
            ? Array.Empty<ReservaCarrito>()
            : await _retencionesCarrito.ListarVigentesPorCarritoAsync(solicitud.CarritoId.Trim(), cancelacion);

        var disponibleDelCarrito = retenciones
            .Where(retencion => retencion.EstaActiva)
            .GroupBy(retencion => (retencion.UbicacionId, retencion.Sku))
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(retencion => retencion.Cantidad));

        var existencias = await _existencias.ListarPorSkusAsync(
            lineas.Select(linea => linea.Sku).ToArray(),
            cancelacion);
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            lineas,
            existencias,
            preferida,
            solicitud.MetodoEntrega,
            disponibleDelCarrito);

        // Recogida en tienda: solo es factible si la tienda elegida cubre todo. Si no,
        // se sugiere la tienda mas cercana a ella que si cubre (o, si ninguna tienda
        // cubre, lo que eligiera el selector).
        if (solicitud.MetodoEntrega == MetodoEntrega.RECOGIDA_TIENDA
            && preferida is not null
            && seleccion.UbicacionId != preferida)
        {
            var sugerida = SelectorSucursalCercana
                .Elegir(lineas, existencias, sucursales, preferida, disponibleDelCarrito)?.Sucursal.Id
                ?? seleccion.UbicacionId;

            return sugerida is null
                ? SinUbicacion(seleccion)
                : Resultado(
                    factible: false,
                    sugerida,
                    existencias,
                    sucursales,
                    referencia,
                    "La sucursal elegida no tiene todos los productos del pedido.");
        }

        return seleccion.UbicacionId is null
            ? SinUbicacion(seleccion)
            : Resultado(factible: true, seleccion.UbicacionId, existencias, sucursales, referencia, motivo: null);
    }

    private static SimulacionReservaDto Resultado(
        bool factible,
        string ubicacionId,
        IReadOnlyList<Existencia> existencias,
        IReadOnlyList<Sucursal> sucursales,
        string? referencia,
        string? motivo)
    {
        var sucursal = sucursales.FirstOrDefault(candidata => candidata.Id == ubicacionId);
        var tipo = sucursal?.TipoUbicacion
                   ?? existencias.FirstOrDefault(existencia => existencia.UbicacionId == ubicacionId)?.TipoUbicacion;

        var esOtraSucursal = tipo == TipoUbicacion.TIENDA
                             && !string.Equals(ubicacionId, referencia, StringComparison.Ordinal);

        return new SimulacionReservaDto(
            factible,
            ubicacionId,
            sucursal?.Nombre ?? ubicacionId,
            tipo,
            esOtraSucursal,
            motivo);
    }

    private static SimulacionReservaDto SinUbicacion(SeleccionUbicacion seleccion)
    {
        var faltantes = string.Join(
            ", ",
            seleccion.Faltantes.Select(faltante =>
                $"{faltante.Sku} (pedido {faltante.Solicitado}, disponible {faltante.Disponible})"));

        var motivo = seleccion.Motivo == MotivoRechazoReserva.SIN_UBICACION_ELEGIBLE
            ? "Algun producto no tiene existencias registradas en ninguna ubicacion."
            : "Ninguna ubicacion tiene todos los productos en la cantidad pedida.";

        if (faltantes.Length > 0)
        {
            motivo += $" Faltantes: {faltantes}.";
        }

        return new SimulacionReservaDto(false, null, null, null, false, motivo);
    }

    private static IReadOnlyList<LineaReservada> Consolidar(IReadOnlyList<LineaSolicitudReserva>? lineas)
    {
        if (lineas is null || lineas.Count == 0)
        {
            throw new ArgumentException("La simulacion requiere al menos una linea.", nameof(lineas));
        }

        if (lineas.Any(linea => string.IsNullOrWhiteSpace(linea.Sku)))
        {
            throw new ArgumentException("Toda linea debe traer un SKU.", nameof(lineas));
        }

        if (lineas.Any(linea => linea.Cantidad <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(lineas), "La cantidad de cada linea debe ser mayor que cero.");
        }

        return lineas
            .GroupBy(linea => linea.Sku, StringComparer.Ordinal)
            .Select(grupo => new LineaReservada(grupo.Key, grupo.Sum(linea => linea.Cantidad)))
            .ToList();
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
