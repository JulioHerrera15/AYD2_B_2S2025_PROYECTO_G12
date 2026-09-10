using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Evento 2 del contrato pagos-eventos-v1. El proveedor rechazo el cobro (fondos
/// insuficientes, tarjeta invalida, etc.): el intento quedo RECHAZADO y el pago
/// RECHAZADO. Es un resultado de negocio, no un error de proceso. Lo consume
/// Ordenes, Auditoria y Consulta de Estado.
/// </summary>
public static class PagoRechazado
{
    public const string EventType = "PagoRechazado";
    public const string RoutingKey = "pagos.pago.rechazado";
    public const string AggregateType = "Pago";
}

/// <summary>Contenido de data del evento PagoRechazado (seccion 7.2 del contrato).</summary>
public sealed record PagoRechazadoData(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    int IntentoNumero,
    EstadoPago Estado,
    string CodigoRespuesta,
    DateTime OcurridoEn);
