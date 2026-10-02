using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Ordenes pide a Credito evaluar una compra a 30 dias. Se emite al pasar la orden a
/// EVALUACION_CREDITO_SOLICITADA, en lugar de CobroSolicitado: una orden a credito
/// nunca llega a Pagos. Credito responde con CreditoAprobado o CreditoRechazado.
/// </summary>
public static class EvaluacionCreditoSolicitada
{
    public const string EventType = "EvaluacionCreditoSolicitada";
    public const string RoutingKey = "ordenes.credito.evaluacion-solicitada";
    public const string AggregateType = "Orden";
}

/// <param name="ClienteId">Email del cliente; Ordenes ya verifico que coincide con el del JWT.</param>
public sealed record EvaluacionCreditoSolicitadaData(
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    DateTime SolicitadaEn);
