using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Facturacion.Api.Application.Facturacion;

/// <summary>
/// Puerto hacia el servicio de certificacion de la SAT. La implementacion real
/// invoca por REST con timeout; en pruebas se usa un doble. La clave de
/// idempotencia es el identificador del DOCUMENTO fiscal (no del intento): un
/// reintento con la misma clave no genera una certificacion duplicada.
/// </summary>
public interface IClienteSat
{
    /// <summary>
    /// Envia el XML a certificar. Lanza <see cref="SatNoDisponibleException" /> ante
    /// 5xx o cuerpo vacio; deja subir <see cref="HttpRequestException" /> y
    /// <see cref="OperationCanceledException" /> (timeout). Un rechazo explicito de
    /// la SAT vuelve como <see cref="ResultadoCertificacionSat" /> con estado
    /// RECHAZADO, no como excepcion.
    /// </summary>
    Task<ResultadoCertificacionSat> CertificarAsync(
        PeticionCertificacionSat peticion,
        string claveIdempotencia,
        CancellationToken cancelacion);
}

/// <summary>Peticion de certificacion hacia la SAT. Lleva el XML ya generado.</summary>
public sealed record PeticionCertificacionSat(
    string Serie,
    string NumeroDocumento,
    string CuiReceptor,
    string Xml,
    string Total,
    Moneda Moneda);

public enum EstadoCertificacionSat
{
    CERTIFICADO,
    RECHAZADO
}

/// <summary>Resultado de una certificacion procesada por la SAT.</summary>
public sealed record ResultadoCertificacionSat(
    EstadoCertificacionSat Estado,
    string? Folio,
    string? NumeroAutorizacion,
    string CodigoRespuesta,
    string Mensaje,
    DateTime CertificadoEn);

/// <summary>La SAT no esta disponible (respondio 5xx o un cuerpo invalido).</summary>
public sealed class SatNoDisponibleException : Exception
{
    public SatNoDisponibleException(string mensaje) : base(mensaje)
    {
    }
}
