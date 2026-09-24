using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Evento 1 del contrato pagos-eventos-v1. El cobro fue aprobado por el proveedor:
/// el intento quedo EXITOSO y el pago APLICADO. Lo consume Ordenes para avanzar la
/// saga (CU-03), y Auditoria y Consulta de Estado. El sobre lo aporta
/// SgoLogic.Contracts.IntegrationEvent y el campo source lo pone el outbox
/// transversal (Outbox:Source = "pagos").
///
/// Restriccion dura (RES-08): este payload no lleva ni llevara PAN, CVV ni fecha de
/// vencimiento. Solo token (que no viaja en el evento), ultimos cuatro y marca.
/// </summary>
public static class PagoAplicado
{
    public const string EventType = "PagoAplicado";
    public const string RoutingKey = "pagos.pago.aplicado";
    public const string AggregateType = "Pago";
}

/// <summary>Contenido de data del evento PagoAplicado (seccion 7.1 del contrato).</summary>
public sealed record PagoAplicadoData(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    int IntentoNumero,
    EstadoPago Estado,
    string CodigoRespuesta,
    string AutorizacionId,
    string? UltimosCuatro,
    string? Marca,
    DateTime OcurridoEn);
