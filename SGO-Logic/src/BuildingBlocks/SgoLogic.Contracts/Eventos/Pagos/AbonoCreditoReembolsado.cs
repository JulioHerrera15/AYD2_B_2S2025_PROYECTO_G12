namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Pagos reembolso a la tarjeta (total o parcialmente) un abono a credito que pidio
/// Credito con ReembolsoAbonoCreditoSolicitado. aggregateId = reembolsoId.
/// </summary>
public static class AbonoCreditoReembolsado
{
    public const string EventType = "AbonoCreditoReembolsado";
    public const string RoutingKey = "pagos.abono-credito.reembolsado";
    public const string AggregateType = "PagoAbonoCredito";
}

public sealed record AbonoCreditoReembolsadoData(
    string ReembolsoId,
    string AbonoId,
    string ClienteId,
    string Moneda,
    decimal Monto,
    string AutorizacionId,
    DateTime OcurridoEn);
