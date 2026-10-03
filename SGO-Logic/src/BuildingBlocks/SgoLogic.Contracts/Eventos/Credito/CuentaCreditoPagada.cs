namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// La cuenta por cobrar de una compra a credito quedo en saldo 0. Puntual indica si
/// se pago a mas tardar en su fecha de vencimiento. aggregateId = cuentaId.
/// </summary>
public static class CuentaCreditoPagada
{
    public const string EventType = "CuentaCreditoPagada";
    public const string RoutingKey = "credito.cuenta.pagada";
    public const string AggregateType = "CuentaPorCobrar";
}

public sealed record CuentaCreditoPagadaData(
    string CuentaId,
    string OrdenId,
    string ClienteId,
    DateTime PagadaEn,
    bool Puntual);
