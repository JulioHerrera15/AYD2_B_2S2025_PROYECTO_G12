namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// El cobro del abono quedo sin resultado cierto (timeout, 5xx, error de red o
/// circuito abierto). Credito permite reintentar con el mismo abonoId. aggregateId = abonoId.
/// </summary>
public static class AbonoCreditoPendiente
{
    public const string EventType = "AbonoCreditoPendiente";
    public const string RoutingKey = "pagos.abono-credito.pendiente";
    public const string AggregateType = "PagoAbonoCredito";
}

public sealed record AbonoCreditoPendienteData(
    string AbonoId,
    string Referencia,
    string ClienteId,
    string MotivoFalla,
    DateTime OcurridoEn);
