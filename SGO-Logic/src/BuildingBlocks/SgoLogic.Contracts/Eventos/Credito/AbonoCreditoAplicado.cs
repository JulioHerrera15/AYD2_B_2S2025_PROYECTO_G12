namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// Credito aplico un abono cobrado: bajo el saldo de la cuenta y el saldo utilizado
/// de la linea. aggregateId = abonoId.
/// </summary>
public static class AbonoCreditoAplicado
{
    public const string EventType = "AbonoCreditoAplicado";
    public const string RoutingKey = "credito.abono.aplicado";
    public const string AggregateType = "AbonoCredito";
}

public sealed record AbonoCreditoAplicadoData(
    string AbonoId,
    string ClienteId,
    string CuentaId,
    string OrdenId,
    decimal Monto,
    string Moneda,
    decimal SaldoPendienteCuenta,
    decimal DisponibleLinea,
    DateTime AplicadoEn);
