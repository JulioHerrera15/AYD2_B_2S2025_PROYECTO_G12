using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Puerto hacia el proveedor de cobros PayKlik. La implementacion real invoca por
/// REST con timeout y va envuelta por el disyuntor (circuit breaker); en pruebas
/// se usa un doble. Nunca recibe ni devuelve numero de tarjeta ni CVV.
/// </summary>
public interface IClientePayKlik
{
    /// <summary>
    /// Solicita un cargo. <paramref name="claveIdempotencia" /> viaja hacia PayKlik
    /// en el header <c>Idempotency-Key</c> y es el identificador del PAGO (no del
    /// intento), para que los reintentos no generen doble cargo.
    ///
    /// Lanza <see cref="PayKlikNoDisponibleException" /> ante 5xx o cuerpo vacio;
    /// deja subir <see cref="HttpRequestException" /> y
    /// <see cref="OperationCanceledException" /> (timeout); lanza
    /// <see cref="CircuitoAbiertoException" /> cuando el disyuntor esta abierto y no
    /// se llego a invocar al proveedor. El servicio de cobro traduce todas esas a
    /// pago PENDIENTE.
    ///
    /// Lanza <see cref="TokenMedioPagoInvalidoException" /> cuando el proveedor
    /// responde que el token no sirve. A diferencia de las anteriores no es una falla
    /// del proveedor: el servicio de cobro lo traduce a pago RECHAZADO.
    /// </summary>
    Task<ResultadoPayKlik> CargarAsync(
        PeticionCargoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion);

    /// <summary>
    /// Solicita un reembolso. <paramref name="claveIdempotencia" /> viaja hacia
    /// PayKlik en el header <c>Idempotency-Key</c> y es el identificador del
    /// reembolso, para que los reintentos no generen doble reembolso.
    ///
    /// Misma semantica de excepciones que <see cref="CargarAsync" />: el servicio
    /// de reembolso traduce las fallas tecnicas a reembolso fallido via outbox.
    /// </summary>
    Task<ResultadoPayKlik> ReembolsoAsync(
        PeticionReembolsoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion);
}

/// <summary>
/// Peticion de cargo hacia PayKlik. Solo el token opaco que emitio PayKlik al
/// tokenizar la tarjeta en el navegador, nunca PAN ni CVV.
/// </summary>
public sealed record PeticionCargoPayKlik(
    string Referencia,
    string Monto,
    Moneda Moneda,
    MedioPago MedioPago,
    string? TokenMedioPago);

/// <summary>Peticion de reembolso hacia PayKlik. Recibe la referencia de la autorizacion original y el monto a reembolsar.</summary>
public sealed record PeticionReembolsoPayKlik(
    string Referencia,
    string Monto,
    Moneda Moneda,
    string AutorizacionOriginal);

public enum EstadoRespuestaPayKlik
{
    APROBADO,
    RECHAZADO
}

/// <summary>Resultado de un cargo procesado por PayKlik.</summary>
public sealed record ResultadoPayKlik(
    string? AutorizacionId,
    EstadoRespuestaPayKlik Estado,
    string CodigoRespuesta,
    string Mensaje,
    string? UltimosCuatro,
    string? Marca,
    DateTime ProcesadoEn);

/// <summary>
/// Codigos de rechazo por problemas con el token del medio de pago. Son codigos
/// internos de Pagos (no del proveedor) y viajan en <c>codigoRespuesta</c> del
/// intento y de <c>PagoRechazado</c>; ninguno coincide con un codigo de rechazo de
/// negocio de PayKlik (por ejemplo "51", fondos insuficientes).
/// </summary>
public static class CodigosToken
{
    /// <summary>Cobro con TARJETA sin token: Pagos lo rechaza sin llamar a PayKlik.</summary>
    public const string Requerido = "TOKEN_REQUERIDO";

    /// <summary>PayKlik no conoce el token.</summary>
    public const string Invalido = "TOKEN_INVALIDO";

    /// <summary>El token expiro antes del cobro.</summary>
    public const string Vencido = "TOKEN_VENCIDO";

    /// <summary>El token ya se uso en otro cargo (son de un solo uso).</summary>
    public const string YaUsado = "TOKEN_YA_USADO";
}

/// <summary>
/// PayKlik respondio 422: el token del medio de pago no sirve (inexistente, vencido
/// o ya usado). Es un error del cliente, no una falla del proveedor: PayKlik esta
/// arriba y respondio bien. Por eso NO cuenta para el disyuntor, NO se reintenta y
/// el servicio de cobro lo traduce a pago RECHAZADO con <see cref="Codigo" />.
/// </summary>
public sealed class TokenMedioPagoInvalidoException : Exception
{
    public TokenMedioPagoInvalidoException(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }

    /// <summary>Uno de los valores de <see cref="CodigosToken" />.</summary>
    public string Codigo { get; }
}

/// <summary>El proveedor no esta disponible (respondio 5xx o un cuerpo invalido).</summary>
public sealed class PayKlikNoDisponibleException : Exception
{
    public PayKlikNoDisponibleException(string mensaje) : base(mensaje)
    {
    }
}

/// <summary>
/// El disyuntor (circuit breaker) hacia PayKlik esta abierto: la llamada HTTP no
/// se realizo. El servicio de cobro lo trata como pago PENDIENTE con motivo
/// <c>CIRCUITO_ABIERTO</c> y latencia cercana a cero, igual que el camino de caida
/// del proveedor: el pago queda PENDIENTE, se genera el enlace de reintento y se
/// publica PagoPendiente por outbox.
/// </summary>
public sealed class CircuitoAbiertoException : Exception
{
    public CircuitoAbiertoException(string mensaje, Exception? interna = null) : base(mensaje, interna)
    {
    }
}
