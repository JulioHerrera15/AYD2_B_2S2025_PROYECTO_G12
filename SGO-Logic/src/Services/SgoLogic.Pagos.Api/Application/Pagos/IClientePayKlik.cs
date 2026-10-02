using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Puerto hacia el proveedor de cobros PayKlik. La implementacion real invoca por
/// REST con timeout y va envuelta por el disyuntor (circuit breaker); en pruebas
/// se usa un doble.
///
/// Desde la tokenizacion interna (A4) el cargo con TARJETA lleva los datos de la
/// tarjeta (descifrados en memoria desde la boveda transitoria) o la referencia de
/// una tarjeta guardada mas su CVV. Ninguna implementacion los registra.
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
    /// rechaza los datos del medio de pago (422/404 con un codigo de
    /// <see cref="CodigosToken" />). A diferencia de las anteriores no es una falla
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

/// <summary>Datos de una tarjeta nueva para el cargo. Solo existen en memoria; <see cref="ToString" /> no los imprime.</summary>
public sealed record TarjetaCargoPayKlik(string Numero, int MesExp, int AnioExp, string Cvv, string Titular)
{
    public override string ToString() => nameof(TarjetaCargoPayKlik) + " { datos de tarjeta omitidos }";
}

/// <summary>
/// Peticion de cargo hacia PayKlik. Para TARJETA lleva <see cref="Tarjeta" /> (tarjeta
/// nueva; <see cref="Guardar" /> pide guardarla en la boveda de PayKlik) o
/// <see cref="ReferenciaTarjeta" /> con <see cref="Cvv" /> (tarjeta guardada). Para los
/// demas medios no lleva datos de tarjeta. <see cref="ToString" /> no imprime nada
/// sensible: el record nunca debe aparecer en un log.
/// </summary>
public sealed record PeticionCargoPayKlik(
    string Referencia,
    string Monto,
    Moneda Moneda,
    MedioPago MedioPago,
    TarjetaCargoPayKlik? Tarjeta = null,
    bool? Guardar = null,
    string? ReferenciaTarjeta = null,
    string? Cvv = null)
{
    public override string ToString() =>
        $"{nameof(PeticionCargoPayKlik)} {{ Referencia = {Referencia}, Monto = {Monto}, Moneda = {Moneda}, MedioPago = {MedioPago} }}";
}

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

/// <summary>
/// Resultado de un cargo procesado por PayKlik. <see cref="ReferenciaTarjeta" /> solo
/// viene cuando el cargo pidio guardar la tarjeta: es la referencia opaca de la
/// boveda de PayKlik (card_pk_...), no un dato de tarjeta.
/// </summary>
public sealed record ResultadoPayKlik(
    string? AutorizacionId,
    EstadoRespuestaPayKlik Estado,
    string CodigoRespuesta,
    string Mensaje,
    string? UltimosCuatro,
    string? Marca,
    DateTime ProcesadoEn,
    string? ReferenciaTarjeta = null);

/// <summary>
/// Codigos de rechazo por problemas con el medio de pago, antes de que PayKlik
/// procese el cargo. Los TOKEN_* son internos de Pagos (boveda transitoria); los
/// TARJETA_* y CVV_INVALIDO los responde PayKlik. Viajan en <c>codigoRespuesta</c>
/// del intento y de <c>PagoRechazado</c>; ninguno coincide con un codigo de rechazo
/// de negocio de PayKlik (por ejemplo "51", fondos insuficientes).
/// </summary>
public static class CodigosToken
{
    /// <summary>Cobro con TARJETA sin token: Pagos lo rechaza sin llamar a PayKlik.</summary>
    public const string Requerido = "TOKEN_REQUERIDO";

    /// <summary>El token no existe, es de otro cliente o sus datos no se pudieron descifrar.</summary>
    public const string Invalido = "TOKEN_INVALIDO";

    /// <summary>El token expiro antes del cobro.</summary>
    public const string Vencido = "TOKEN_VENCIDO";

    /// <summary>El token ya se uso en un cargo con respuesta definitiva.</summary>
    public const string YaUsado = "TOKEN_YA_USADO";

    /// <summary>PayKlik no acepto los datos de la tarjeta (422).</summary>
    public const string TarjetaInvalida = "TARJETA_INVALIDA";

    /// <summary>PayKlik no acepto el CVV de la tarjeta guardada (422).</summary>
    public const string CvvInvalido = "CVV_INVALIDO";

    /// <summary>PayKlik reporto la tarjeta vencida (422).</summary>
    public const string TarjetaVencida = "TARJETA_VENCIDA";

    /// <summary>PayKlik ya no tiene la referencia de la tarjeta guardada (404): se desactiva.</summary>
    public const string TarjetaNoEncontrada = "TARJETA_NO_ENCONTRADA";

    /// <summary>La tarjeta guardada del token ya no esta activa en Pagos.</summary>
    public const string TarjetaNoValida = "TARJETA_NO_VALIDA";

    /// <summary>Los que puede responder PayKlik como 422 o 404 con cuerpo { codigo }.</summary>
    public static readonly IReadOnlyList<string> DelProveedor = new[]
    {
        TarjetaInvalida, CvvInvalido, TarjetaVencida, TarjetaNoEncontrada
    };
}

/// <summary>
/// PayKlik rechazo los datos del medio de pago (422 o 404 con un codigo de
/// <see cref="CodigosToken" />). Es un error del cliente, no una falla del proveedor:
/// PayKlik esta arriba y respondio bien. Por eso NO cuenta para el disyuntor, NO se
/// reintenta y el servicio de cobro lo traduce a pago RECHAZADO con <see cref="Codigo" />.
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
