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
    /// </summary>
    Task<ResultadoPayKlik> CargarAsync(
        PeticionCargoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion);
}

/// <summary>Peticion de cargo hacia PayKlik. Solo token opaco, nunca PAN ni CVV.</summary>
public sealed record PeticionCargoPayKlik(
    string Referencia,
    string Monto,
    Moneda Moneda,
    MedioPago MedioPago,
    string? TokenMedioPago);

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
