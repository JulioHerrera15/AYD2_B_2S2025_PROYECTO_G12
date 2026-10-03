namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// El reembolso de un abono a credito no se pudo completar (rechazo del proveedor,
/// reintentos agotados, sin cobro aplicado o monto mayor a lo cobrado). Credito deja el
/// abono para atencion manual. aggregateId = reembolsoId.
/// </summary>
public static class ReembolsoAbonoCreditoFallido
{
    public const string EventType = "ReembolsoAbonoCreditoFallido";
    public const string RoutingKey = "pagos.abono-credito.reembolso-fallido";
    public const string AggregateType = "PagoAbonoCredito";
}

public sealed record ReembolsoAbonoCreditoFallidoData(
    string ReembolsoId,
    string AbonoId,
    string ClienteId,
    string Moneda,
    decimal Monto,
    string MotivoFalla,
    DateTime OcurridoEn);
