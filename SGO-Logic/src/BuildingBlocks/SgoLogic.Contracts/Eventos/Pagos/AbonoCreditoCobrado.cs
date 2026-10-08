namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// El proveedor aprobo el cobro de un abono a credito. Lo consume Credito para
/// aplicar el abono. Un abono nunca emite PagoAplicado. aggregateId = abonoId.
/// </summary>
public static class AbonoCreditoCobrado
{
    public const string EventType = "AbonoCreditoCobrado";
    public const string RoutingKey = "pagos.abono-credito.cobrado";
    public const string AggregateType = "PagoAbonoCredito";
}

public sealed record AbonoCreditoCobradoData(
    string AbonoId,
    string Referencia,
    string ClienteId,
    string Moneda,
    decimal Monto,
    string AutorizacionId,
    string? UltimosCuatro,
    string? Marca,
    DateTime OcurridoEn);
