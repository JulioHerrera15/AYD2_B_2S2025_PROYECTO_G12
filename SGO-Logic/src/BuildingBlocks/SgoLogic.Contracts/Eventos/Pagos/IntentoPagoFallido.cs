using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Evento 6 del contrato pagos-eventos-v1 (v1.3). Una llamada de cobro a PayKlik
/// no obtuvo respuesta util (timeout, 5xx, red o circuito abierto) y su intento
/// quedo FALLIDO. Se emite una vez por cada intento fallido, incluidos los
/// intermedios que el reintento con backoff supera, para que cada intento fallido
/// quede auditado (EAC-01). Es solo de auditoria: no es un evento de resultado del
/// cobro y no lo consume la saga. El resultado sigue siendo PagoAplicado,
/// PagoRechazado o PagoPendiente, uno por cobro. Lo consume Auditoria por su
/// binding "#".
///
/// El agregado es el intento (<see cref="AggregateType" />, id = ordenId) y la
/// sequence es el numero de intento: asi no consume la secuencia del pago, que
/// siguen usando los eventos de resultado.
/// </summary>
public static class IntentoPagoFallido
{
    public const string EventType = "IntentoPagoFallido";
    public const string RoutingKey = "pagos.intento.fallido";
    public const string AggregateType = "IntentoPago";
}

/// <summary>Contenido de data del evento IntentoPagoFallido (seccion 7.6 del contrato).</summary>
public sealed record IntentoPagoFallidoData(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    int IntentoNumero,
    string CodigoRespuesta,
    string MotivoFalla,
    int LatenciaMs,
    bool SeReintenta,
    DateTime OcurridoEn);
