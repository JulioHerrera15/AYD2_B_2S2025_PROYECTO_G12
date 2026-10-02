namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito aprobo la compra: aparto el monto de la linea y emitio la cuenta por
/// cobrar (cargoId). Ordenes lleva la orden a CREDITO_APROBADO y, en la misma
/// transaccion, a CONFIRMADA_PENDIENTE_FISCAL. aggregateId = ordenId.
/// </summary>
public static class CreditoAprobado
{
    public const string EventType = "CreditoAprobado";
    public const string RoutingKey = "credito.evaluacion.aprobada";
    public const string AggregateType = "EvaluacionCredito";
}

/// <param name="Moneda">Codigo ISO de la moneda (GTQ).</param>
/// <param name="FechaVencimiento">Fecha limite de pago de la cuenta por cobrar.</param>
/// <param name="DisponibleRestante">Disponible de la linea despues de apartar este monto.</param>
public sealed record CreditoAprobadoData(
    string OrdenId,
    string ClienteId,
    string CargoId,
    string DecisionId,
    decimal Monto,
    string Moneda,
    DateTime FechaVencimiento,
    decimal LimiteAprobado,
    decimal DisponibleRestante,
    DateTime DecididoEn);
