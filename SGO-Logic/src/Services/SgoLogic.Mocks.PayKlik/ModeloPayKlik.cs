namespace SgoLogic.Mocks.PayKlik;

/// <summary>
/// Comportamiento configurable del mock. NORMAL responde rapido y aprueba; LENTO
/// duerme LatenciaMs antes de responder; CAIDO responde 503 sin procesar; RECHAZO
/// responde 200 con estado RECHAZADO; INTERMITENTE falla (como CAIDO) segun
/// ProbabilidadFallo.
/// </summary>
public enum ModoMock
{
    NORMAL,
    LENTO,
    CAIDO,
    RECHAZO,
    INTERMITENTE
}

/// <summary>Peticion de cargo. Nunca recibe numero de tarjeta ni CVV: solo un token opaco.</summary>
public sealed record PeticionCargo(
    string Referencia,
    string Monto,
    string Moneda,
    string MedioPago,
    string? TokenMedioPago);

/// <summary>Respuesta de un cargo procesado. Nunca devuelve numero de tarjeta ni CVV.</summary>
public sealed record RespuestaCargo(
    string? AutorizacionId,
    string Estado,
    string CodigoRespuesta,
    string Mensaje,
    string? UltimosCuatro,
    string? Marca,
    DateTime ProcesadoEn);

/// <summary>
/// Cambio de modo del mock via PUT /admin/modo. <c>LatenciaMs</c> y
/// <c>ProbabilidadFallo</c> son opcionales: <c>null</c> significa "no enviado, se
/// conserva el valor actual"; cualquier valor presente (incluido <c>0</c>) se
/// aplica tal cual.
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

/// <summary>Estado administrativo del mock (GET /admin/estado).</summary>
public sealed record EstadoAdminDto(
    ModoMock Modo,
    int LatenciaMs,
    double ProbabilidadFallo,
    int ClavesIdempotencia);

/// <summary>Un cargo recibido en esta ejecucion, para inspeccion en la demo.</summary>
public sealed record CargoRegistrado(
    string Referencia,
    string Monto,
    string Moneda,
    string MedioPago,
    string Resultado,
    string? AutorizacionId,
    DateTime RecibidoEn);
