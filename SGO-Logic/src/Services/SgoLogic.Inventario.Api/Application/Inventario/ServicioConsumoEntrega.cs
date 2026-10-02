using Microsoft.Extensions.Logging;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Convierte la reserva de una orden en salida fisica cuando el operador envia el
/// pedido a ruta (EN_TRANSITO o LISTA_PARA_RECOGER); la entrega (ENTREGADA) queda
/// como respaldo si el paso a ruta nunca llego.
///
/// Solo consume reservas que retienen stock (ACTIVA o CONFIRMADA). Por cada linea
/// resta exactamente su cantidad de la fisica y de la reservada de la existencia,
/// registra el movimiento DESPACHO y marca la reserva ENTREGADA.
///
/// No llama a <c>SaveChangesAsync</c>: la transaccion la cierra quien llama, para
/// que el consumo y la marca de mensaje procesado (en el consumidor) o el consumo
/// solo (en el endpoint REST) queden en un unico SaveChanges.
/// </summary>
public sealed class ServicioConsumoEntrega
{
    private readonly IRepositorioReservas _reservas;
    private readonly IRepositorioExistencias _existencias;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly ILogger<ServicioConsumoEntrega> _logger;

    public ServicioConsumoEntrega(
        IRepositorioReservas reservas,
        IRepositorioExistencias existencias,
        IRepositorioMovimientos movimientos,
        ILogger<ServicioConsumoEntrega> logger)
    {
        _reservas = reservas;
        _existencias = existencias;
        _movimientos = movimientos;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve false solo si la orden no tiene reserva. En cualquier otro caso
    /// devuelve true: consumio, ya estaba consumida (idempotente) o la reserva ya no
    /// retenia stock (EXPIRADA o LIBERADA) y no hay nada que descontar.
    /// </summary>
    public async Task<bool> ConsumirAsync(string ordenId, CancellationToken cancelacion)
    {
        var reserva = await _reservas.ObtenerPorOrdenAsync(ordenId, cancelacion);
        if (reserva is null)
        {
            return false;
        }

        if (reserva.Estado == EstadoReserva.ENTREGADA)
        {
            return true;
        }

        if (!reserva.RetieneStock)
        {
            // Las unidades ya volvieron al disponible (TTL o compensacion): descontar
            // ahora sacaria del almacen stock que ya se ofrecio a otras ordenes.
            _logger.LogWarning(
                "Se pidio consumir la reserva {ReservaId} de la orden {OrdenId}, que esta en {Estado}; no se descuenta nada.",
                reserva.Id,
                ordenId,
                reserva.Estado);

            return true;
        }

        var ahora = DateTime.UtcNow;

        foreach (var linea in reserva.Lineas)
        {
            var existencia = await _existencias.ObtenerAsync(linea.Sku, reserva.UbicacionId, cancelacion)
                ?? throw new InvalidOperationException(
                    $"No existe {linea.Sku} en {reserva.UbicacionId} para la orden {ordenId}.");

            existencia.ConsumirReservada(linea.Cantidad);

            await _movimientos.AgregarAsync(
                new MovimientoExistencia(
                    existencia.Sku,
                    existencia.UbicacionId,
                    -linea.Cantidad,
                    existencia.CantidadFisica,
                    MotivoMovimiento.DESPACHO,
                    ahora),
                cancelacion);
        }

        reserva.MarcarEntregada();

        _logger.LogInformation(
            "Reserva {ReservaId} de la orden {OrdenId} consumida: sus unidades salieron del almacen {UbicacionId}.",
            reserva.Id,
            ordenId,
            reserva.UbicacionId);

        return true;
    }
}
