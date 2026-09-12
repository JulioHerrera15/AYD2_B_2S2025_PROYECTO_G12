using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Evento de resultado de reembolso. El reembolso fue procesado exitosamente
/// por el proveedor. Lo emite Pagos al consumir DevolucionRegistrada y recibir
/// respuesta positiva del proveedor.
/// </summary>
public static class ReembolsoAplicado
{
    public const string EventType = "ReembolsoAplicado";
    public const string RoutingKey = "pagos.reembolso.aplicado";
    public const string AggregateType = "Pago";
}

/// <summary>Contenido de data del evento ReembolsoAplicado.</summary>
public sealed record ReembolsoAplicadoData(
    string DevolucionId,
    string OrdenOriginalId,
    string ClienteId,
    Moneda Moneda,
    decimal Monto,
    string CodigoRespuesta,
    string AutorizacionId,
    DateTime OcurridoEn);
