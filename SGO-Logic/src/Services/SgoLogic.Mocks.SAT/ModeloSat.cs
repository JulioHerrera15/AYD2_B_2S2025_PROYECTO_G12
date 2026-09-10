namespace SgoLogic.Mocks.SAT;

/// <summary>
/// Comportamiento configurable del mock de la SAT. NORMAL responde rapido y
/// certifica; LENTO duerme LatenciaMs antes de responder; CAIDO responde 503 sin
/// procesar; RECHAZO responde 200 con estado RECHAZADO. En cualquier modo,
/// ProbabilidadFallo (0..1) fuerza al azar un 503 antes de aplicar el modo, para
/// simular intermitencia sin un modo aparte.
/// </summary>
public enum ModoMock
{
    NORMAL,
    LENTO,
    CAIDO,
    RECHAZO
}

/// <summary>
/// Peticion de certificacion. Recibe el XML fiscal ya generado y los datos
/// minimos para identificarlo. Nunca recibe datos de tarjeta.
/// </summary>
public sealed record PeticionCertificacion(
    string Serie,
    string NumeroDocumento,
    string CuiReceptor,
    string Xml,
    string Total,
    string Moneda);

/// <summary>Respuesta de una certificacion procesada. Devuelve folio y autorizacion, o un rechazo.</summary>
public sealed record RespuestaCertificacion(
    string? Folio,
    string? NumeroAutorizacion,
    string Estado,
    string CodigoRespuesta,
    string Mensaje,
    DateTime CertificadoEn);

/// <summary>
/// Cambio de modo via PUT /admin/modo. <c>LatenciaMs</c> y <c>ProbabilidadFallo</c>
/// son opcionales: <c>null</c> significa "no enviado, se conserva el valor actual";
/// cualquier valor presente (incluido <c>0</c>) se aplica tal cual. Un negativo o un
/// valor fuera de 0..1 se descarta.
/// </summary>
public sealed record CambioModo(
    string Modo,
    int? LatenciaMs,
    double? ProbabilidadFallo);

/// <summary>Configuracion vigente del mock.</summary>
public sealed record ConfiguracionMock(
    ModoMock Modo,
    int LatenciaMs,
    double ProbabilidadFallo);

/// <summary>
/// Estado administrativo del mock (GET /admin/estado): la configuracion vigente y
/// las claves de idempotencia guardadas en esta ejecucion.
/// </summary>
public sealed record EstadoAdminDto(
    ModoMock Modo,
    int LatenciaMs,
    double ProbabilidadFallo,
    int ClavesIdempotencia,
    IReadOnlyList<string> Claves);

/// <summary>Una certificacion recibida en esta ejecucion, para inspeccion en la demo.</summary>
public sealed record CertificacionRegistrada(
    string Serie,
    string NumeroDocumento,
    string CuiReceptor,
    string Total,
    string Moneda,
    string Resultado,
    string? NumeroAutorizacion,
    DateTime RecibidoEn);
