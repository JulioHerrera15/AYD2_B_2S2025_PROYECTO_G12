using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Pagos reembolso a la tarjeta lo cobrado por una orden que se cancelo despues de
/// pagarse (OrdenCancelada con requiereReembolso). Lo consume Ordenes para mostrar el
/// estado del reembolso. aggregateId = ordenId.
/// </summary>
public static class ReembolsoCancelacionAplicado
{
    public const string EventType = "ReembolsoCancelacionAplicado";
    public const string RoutingKey = "pagos.reembolso-cancelacion.aplicado";
    public const string AggregateType = "Pago";
}

public sealed record ReembolsoCancelacionAplicadoData(
    string OrdenId,
    string ClienteId,
    Moneda Moneda,
    decimal Monto,
    string AutorizacionId,
    DateTime OcurridoEn);
