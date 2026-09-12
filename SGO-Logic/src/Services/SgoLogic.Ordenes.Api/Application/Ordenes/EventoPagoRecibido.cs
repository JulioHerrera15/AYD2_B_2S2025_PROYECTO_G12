namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Datos ya interpretados de un evento de resultado de Pagos que llega por la cola
/// <c>ordenes.pagos</c>. El consumidor arma este registro a partir del sobre y su
/// <c>data</c>; el manejador de la saga solo depende de estos campos, no del
/// transporte.
/// </summary>
/// <param name="MessageId">Campo <c>eventId</c> del sobre. Ancla de idempotencia y causa del evento derivado.</param>
/// <param name="TransactionId">Campo <c>transactionId</c> del sobre. Se hereda al evento que emite Ordenes.</param>
/// <param name="TipoEvento">Campo <c>eventType</c> del sobre (<c>PagoAplicado</c>, <c>PagoRechazado</c> o <c>PagoPendiente</c>).</param>
/// <param name="OrdenId">Orden sobre la que avanza o se compensa la saga.</param>
/// <param name="Motivo">Texto de negocio para <c>OrdenEstadoCambiado</c>; <c>null</c> en el camino feliz.</param>
/// <param name="AutorizacionId">
/// Campo <c>autorizacionId</c> de <c>PagoAplicado.data</c>: el identificador que
/// devuelve el proveedor para ese cobro. Es el unico identificador real de pago
/// que trae el contrato (Pagos es un agregado por orden: su envelope no manda un
/// pagoId propio). <c>null</c> para PagoRechazado y PagoPendiente, que no lo traen.
/// </param>
public sealed record EventoPagoRecibido(
    string MessageId,
    string TransactionId,
    string TipoEvento,
    string OrdenId,
    string? Motivo,
    string? AutorizacionId = null,
    string? DevolucionId = null);

/// <summary>Desenlace de aplicar un evento de Pagos a la saga de la orden.</summary>
public enum ResultadoEventoPago
{
    /// <summary>La transicion se aplico y se encolaron los eventos derivados.</summary>
    Aplicado,

    /// <summary>El messageId ya estaba registrado: no se aplico nada.</summary>
    Duplicado,

    /// <summary>
    /// Transicion invalida o evento desconocido: se registra un warning y se hace
    /// ack sin reencolar (un mensaje que nunca va a aplicar no debe entrar en bucle).
    /// </summary>
    Ignorado
}
