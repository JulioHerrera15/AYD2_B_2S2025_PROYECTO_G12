namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// La cuenta paso su fecha de vencimiento con saldo: queda VENCIDA y la linea del
/// cliente en pausa (BLOQUEADA) hasta que la pague. Sin intereses ni recargos.
/// aggregateId = cuentaId.
/// </summary>
public static class CuentaCreditoVencida
{
    public const string EventType = "CuentaCreditoVencida";
    public const string RoutingKey = "credito.cuenta.vencida";
    public const string AggregateType = "CuentaPorCobrar";
}

public sealed record CuentaCreditoVencidaData(
    string CuentaId,
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    decimal SaldoPendiente,
    string Moneda,
    DateTime VenceEn,
    DateTime VencidaEn);
