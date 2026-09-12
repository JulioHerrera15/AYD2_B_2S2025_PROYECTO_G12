using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Evento de resultado de reembolso fallido. El proveedor rechazo o no respondio
/// el reembolso. Lo emite Pagos al consumir DevolucionRegistrada y recibir
/// respuesta negativa o error del proveedor.
/// </summary>
public static class ReembolsoFallido
{
    public const string EventType = "ReembolsoFallido";
    public const string RoutingKey = "pagos.reembolso.fallido";
    public const string AggregateType = "Pago";
}

/// <summary>Contenido de data del evento ReembolsoFallido.</summary>
public sealed record ReembolsoFallidoData(
    string DevolucionId,
    string OrdenOriginalId,
    string ClienteId,
    Moneda Moneda,
    decimal Monto,
    string CodigoRespuesta,
    string MotivoFalla,
    DateTime OcurridoEn);
