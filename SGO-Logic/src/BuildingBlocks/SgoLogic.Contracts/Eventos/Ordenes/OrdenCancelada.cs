using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 9 del contrato ordenes-eventos-v1. Cancelacion por cualquier causa,
/// incluida la de una orden ya confirmada. Se emite al pasar la orden a CANCELADA o
/// a CANCELADA_RESERVA_VENCIDA. Prohibido como fuente de estado (seccion 6 del
/// contrato).
/// </summary>
public static class OrdenCancelada
{
    public const string EventType = "OrdenCancelada";
    public const string RoutingKey = "ordenes.orden.cancelada";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento OrdenCancelada (seccion 7.9 del contrato).</summary>
public sealed record OrdenCanceladaData(
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    string? TiendaDestinoId,
    bool EstabaConfirmada,
    MotivoCancelacion Motivo,
    string? Detalle,
    bool RequiereReembolso,
    string? PagoId,
    string? DocumentoFiscalId,
    Moneda? Moneda,
    decimal? MontoReembolso,
    DateTime CanceladaEn);
