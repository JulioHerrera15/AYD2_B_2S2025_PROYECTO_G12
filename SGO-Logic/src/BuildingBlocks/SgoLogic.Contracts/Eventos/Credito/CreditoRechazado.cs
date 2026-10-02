namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito rechazo la compra; no aparto cupo ni emitio cargo. Ordenes lleva la orden
/// a RECHAZADA y pide liberar la reserva. aggregateId = ordenId.
/// </summary>
public static class CreditoRechazado
{
    public const string EventType = "CreditoRechazado";
    public const string RoutingKey = "credito.evaluacion.rechazada";
    public const string AggregateType = "EvaluacionCredito";
}

/// <param name="CodigoMotivo">
/// SIN_HISTORIAL, INCIDENTE_GRAVE, ANTIGUEDAD_INSUFICIENTE, DEVOLUCIONES_EXCESIVAS,
/// PUNTAJE_INSUFICIENTE, CUPO_INSUFICIENTE, LINEA_BLOQUEADA u ORDEN_CANCELADA. Uso interno.
/// </param>
/// <param name="MensajeCliente">Texto en lenguaje llano para mostrar al cliente.</param>
public sealed record CreditoRechazadoData(
    string OrdenId,
    string ClienteId,
    string DecisionId,
    string CodigoMotivo,
    string MensajeCliente,
    DateTime DecididoEn);
