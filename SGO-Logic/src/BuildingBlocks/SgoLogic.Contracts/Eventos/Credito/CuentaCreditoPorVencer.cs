namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Recordatorio: la cuenta vence en pocos dias y aun tiene saldo. Se emite una sola
/// vez por cuenta. aggregateId = cuentaId.
/// </summary>
public static class CuentaCreditoPorVencer
{
    public const string EventType = "CuentaCreditoPorVencer";
    public const string RoutingKey = "credito.cuenta.por-vencer";
    public const string AggregateType = "CuentaPorCobrar";
}

public sealed record CuentaCreditoPorVencerData(
    string CuentaId,
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    decimal SaldoPendiente,
    string Moneda,
    DateTime VenceEn,
    int DiasRestantes);
