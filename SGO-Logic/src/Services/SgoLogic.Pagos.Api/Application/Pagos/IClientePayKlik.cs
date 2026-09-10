using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Puerto hacia el proveedor de cobros PayKlik. La implementacion real invoca por
/// REST con timeout; en pruebas se usa un doble. Nunca recibe ni devuelve numero
/// de tarjeta ni CVV.
/// </summary>
public interface IClientePayKlik
{
    /// <summary>
    /// Solicita un cargo. Lanza <see cref="PayKlikNoDisponibleException" /> ante
    /// 5xx o cuerpo vacio; deja subir <see cref="HttpRequestException" /> y
    /// <see cref="OperationCanceledException" /> (timeout) para que el servicio las
    /// trate como pago PENDIENTE.
    /// </summary>
    Task<ResultadoPayKlik> CargarAsync(PeticionCargoPayKlik peticion, CancellationToken cancelacion);
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
