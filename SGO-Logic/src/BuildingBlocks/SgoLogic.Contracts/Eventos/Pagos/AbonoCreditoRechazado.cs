namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// El cobro del abono fue rechazado (proveedor, tarjeta o token invalido, vencido o
/// ajeno). No hubo cargo. aggregateId = abonoId.
/// </summary>
public static class AbonoCreditoRechazado
{
    public const string EventType = "AbonoCreditoRechazado";
    public const string RoutingKey = "pagos.abono-credito.rechazado";
    public const string AggregateType = "PagoAbonoCredito";
}

public sealed record AbonoCreditoRechazadoData(
    string AbonoId,
    string Referencia,
    string ClienteId,
    string CodigoRespuesta,
    DateTime OcurridoEn);
