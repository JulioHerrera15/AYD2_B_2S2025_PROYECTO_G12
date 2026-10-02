using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Libera una reserva por compensacion de la saga (HU-003): un cobro rechazado o
/// una orden cancelada piden devolver las unidades sin esperar a que venza el TTL.
///
/// Sin esto, un pago rechazado deja el stock retenido hasta 15 minutos: el cliente
/// siguiente ve "sin existencias" de mercaderia que en realidad ya esta libre. El
/// barrido de HU-002 lo arregla tarde; esto lo arregla en el momento.
///
/// No llama a <c>SaveChangesAsync</c>: lo hace el consumidor, para que la
/// liberacion, el evento y la marca de mensaje procesado queden en una sola
/// transaccion.
/// </summary>
public sealed class ServicioLiberacionReservas
{
    private readonly IRepositorioReservas _reservas;
    private readonly IRepositorioExistencias _existencias;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<ServicioLiberacionReservas> _logger;

    public ServicioLiberacionReservas(
        IRepositorioReservas reservas,
        IRepositorioExistencias existencias,
        IOutboxWriter outbox,
        ILogger<ServicioLiberacionReservas> logger)
    {
        _reservas = reservas;
        _existencias = existencias;
        _outbox = outbox;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve al disponible las unidades de la reserva de una orden, localizada
    /// por su OrdenId (indice unico). Libera reservas ACTIVA o CONFIRMADA (la orden
    /// pudo cancelarse despues de pagada). Idempotente: una reserva que ya no retiene
    /// stock (LIBERADA, EXPIRADA o ENTREGADA) no se toca ni re-emite.
    /// </summary>
    public async Task<bool> LiberarAsync(
        string ordenId,
        MotivoLiberacionReserva motivo,
        CancellationToken cancelacion)
    {
        var reserva = await _reservas.ObtenerPorOrdenAsync(ordenId, cancelacion);

        if (reserva is null)
        {
            // Puede pasar legitimamente: la orden se cancelo antes de que Inventario
            // llegara a reservar, o la reserva fue rechazada por falta de stock. No
            // es un error que deba reintentarse.
            _logger.LogInformation(
                "Se pidio liberar la reserva de la orden {OrdenId}, que no tiene ninguna. Nada que hacer.",
                ordenId);

            return false;
        }

        if (!reserva.RetieneStock)
        {
            // Ya la libero el barrido de TTL (HU-002) o un mensaje anterior, o sus
            // unidades ya salieron a ruta. No hay nada que devolver; re-emitir solo
            // confundiria a los consumidores.
            _logger.LogInformation(
                "La reserva {ReservaId} de la orden {OrdenId} ya estaba en {Estado}; no se libera de nuevo.",
                reserva.Id,
                ordenId,
                reserva.Estado);

            return false;
        }

        foreach (var linea in reserva.Lineas)
        {
            var existencia = await _existencias.ObtenerAsync(linea.Sku, reserva.UbicacionId, cancelacion);

            if (existencia is null)
            {
                // Mismo criterio que la expiracion: la reserva se libera igual, no
                // puede quedar reteniendo stock para siempre, pero queda constancia.
                _logger.LogWarning(
                    "La reserva {ReservaId} referencia el SKU {Sku} en {UbicacionId}, que ya no tiene existencia registrada.",
                    reserva.Id,
                    linea.Sku,
                    reserva.UbicacionId);

                continue;
            }

            existencia.LiberarReserva(linea.Cantidad);
        }

        reserva.Liberar();

        _outbox.Encolar(
            ReservaInventarioLiberada.EventType,
            ReservaInventarioLiberada.AggregateType,
            reserva.Id,
            reserva.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            ReservaInventarioLiberada.RoutingKey,
            new ReservaInventarioLiberadaData(
                ReservaId: reserva.Id,
                OrdenId: reserva.OrdenId,
                UbicacionId: reserva.UbicacionId,
                Motivo: motivo,
                Lineas: reserva.Lineas
                    .Select(linea => new ReservaConfirmadaLineaData(linea.Sku, linea.Cantidad))
                    .ToList(),
                LiberadaEn: DateTime.UtcNow));

        _logger.LogInformation(
            "Reserva {ReservaId} de la orden {OrdenId} liberada por {Motivo}; sus unidades volvieron al disponible.",
            reserva.Id,
            ordenId,
            motivo);

        return true;
    }
}
