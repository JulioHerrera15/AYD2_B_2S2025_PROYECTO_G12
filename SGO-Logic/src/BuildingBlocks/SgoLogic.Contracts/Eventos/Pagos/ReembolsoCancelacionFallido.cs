using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// El reembolso de una orden cancelada no se pudo completar: el proveedor lo rechazo
/// o se agotaron los reintentos ante fallas tecnicas. Queda para atencion manual.
/// aggregateId = ordenId.
/// </summary>
public static class ReembolsoCancelacionFallido
{
    public const string EventType = "ReembolsoCancelacionFallido";
    public const string RoutingKey = "pagos.reembolso-cancelacion.fallido";
    public const string AggregateType = "Pago";
}

public sealed record ReembolsoCancelacionFallidoData(
    string OrdenId,
    string ClienteId,
    Moneda Moneda,
    decimal Monto,
    string MotivoFalla,
    DateTime OcurridoEn);
