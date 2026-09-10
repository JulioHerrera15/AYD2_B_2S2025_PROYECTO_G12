using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Evento 3 del contrato pagos-eventos-v1. PayKlik no resolvio el cobro (timeout,
/// 5xx o error de red): el intento quedo FALLIDO y el pago PENDIENTE. La orden se
/// preserva y Pagos genera un enlace de reintento con token opaco y expiracion
/// (EV-28, ADR-010). Lo consume Ordenes (que emite el enlace al cliente),
/// Auditoria y Consulta de Estado.
/// </summary>
public static class PagoPendiente
{
    public const string EventType = "PagoPendiente";
    public const string RoutingKey = "pagos.pago.pendiente";
    public const string AggregateType = "Pago";
}

/// <summary>Contenido de data del evento PagoPendiente (seccion 7.3 del contrato).</summary>
public sealed record PagoPendienteData(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    int IntentoNumero,
    EstadoPago Estado,
    string CodigoRespuesta,
    string MotivoFalla,
    EnlaceReintentoData EnlaceReintento,
    DateTime OcurridoEn);

/// <summary>
/// Enlace de reintento de cobro. El token es opaco: identifica el enlace sin
/// exponer nada del medio de pago. La ruta es relativa al servicio Pagos.
/// </summary>
public sealed record EnlaceReintentoData(
    string Token,
    string Url,
    DateTime ExpiraEn);
