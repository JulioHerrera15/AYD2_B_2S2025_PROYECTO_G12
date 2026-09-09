using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 4 del contrato ordenes-eventos-v1. Compensacion de la saga: Ordenes pide
/// a Inventario liberar una reserva. Distinto de la expiracion por TTL, que la
/// publica Inventario. Se emite cuando la transicion viene marcada como
/// compensacion.
/// </summary>
public static class LiberacionReservaSolicitada
{
    public const string EventType = "LiberacionReservaSolicitada";
    public const string RoutingKey = "ordenes.reserva.liberacion-solicitada";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento LiberacionReservaSolicitada (seccion 7.4 del contrato).</summary>
public sealed record LiberacionReservaSolicitadaData(
    string OrdenId,
    string ReservaId,
    MotivoLiberacionReserva Motivo,
    DateTime SolicitadaEn);
