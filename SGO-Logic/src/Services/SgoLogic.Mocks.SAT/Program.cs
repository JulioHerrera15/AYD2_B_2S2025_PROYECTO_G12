using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Mocks.SAT;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    opciones.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<EstadoMock>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

// Certificacion fiscal. Recibe el XML ya generado y devuelve folio o rechazo.
// El header Idempotency-Key es el identificador del DOCUMENTO fiscal: misma clave
// devuelve la misma respuesta sin volver a certificar.
app.MapPost("/v1/certificaciones", async (
    PeticionCertificacion peticion,
    EstadoMock estado,
    [FromHeader(Name = "Idempotency-Key")] string? claveIdempotencia) =>
{
    var configuracion = estado.Configuracion;
    var recibidoEn = DateTime.UtcNow;

    if (!string.IsNullOrWhiteSpace(claveIdempotencia) &&
        estado.Idempotencia.TryObtener(claveIdempotencia, out var previa))
    {
        estado.Registrar(new CertificacionRegistrada(
            peticion.Serie, peticion.NumeroDocumento, peticion.CuiReceptor, peticion.Total, peticion.Moneda,
            Resultado: "IDEMPOTENTE", NumeroAutorizacion: previa.NumeroAutorizacion, RecibidoEn: recibidoEn));

        return Results.Ok(previa);
    }

    // ProbabilidadFallo se evalua antes del modo: cualquier modo puede caer al azar.
    var caidaAleatoria = configuracion.ProbabilidadFallo > 0
        && Random.Shared.NextDouble() < configuracion.ProbabilidadFallo;

    // latenciaMs igual a 0 se aplica como cero: no hay espera.
    if (configuracion.Modo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs);
    }

    if (caidaAleatoria || configuracion.Modo == ModoMock.CAIDO)
    {
        estado.Registrar(new CertificacionRegistrada(
            peticion.Serie, peticion.NumeroDocumento, peticion.CuiReceptor, peticion.Total, peticion.Moneda,
            Resultado: "NO_PROCESADO", NumeroAutorizacion: null, RecibidoEn: recibidoEn));

        return Results.Problem(
            title: "SAT no disponible",
            detail: "El servicio de la SAT no proceso la certificacion.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var certificado = configuracion.Modo != ModoMock.RECHAZO;

    var respuesta = certificado
        ? new RespuestaCertificacion(
            Folio: GenerarFolio(),
            NumeroAutorizacion: GenerarAutorizacion(),
            Estado: "CERTIFICADO",
            CodigoRespuesta: "00",
            Mensaje: "Documento certificado",
            CertificadoEn: recibidoEn)
        : new RespuestaCertificacion(
            Folio: null,
            NumeroAutorizacion: null,
            Estado: "RECHAZADO",
            CodigoRespuesta: "RECH-01",
            Mensaje: "Documento rechazado por la SAT (modo RECHAZO).",
            CertificadoEn: recibidoEn);

    estado.Registrar(new CertificacionRegistrada(
        peticion.Serie, peticion.NumeroDocumento, peticion.CuiReceptor, peticion.Total, peticion.Moneda,
        Resultado: respuesta.Estado, NumeroAutorizacion: respuesta.NumeroAutorizacion, RecibidoEn: recibidoEn));

    // Se guarda para idempotencia toda respuesta efectivamente procesada (200),
    // tanto la certificada como el rechazo explicito.
    if (!string.IsNullOrWhiteSpace(claveIdempotencia))
    {
        estado.Idempotencia.ObtenerOAgregar(claveIdempotencia, () => respuesta);
    }

    return Results.Ok(respuesta);
});

app.MapPut("/admin/modo", (CambioModo cambio, EstadoMock estado) =>
{
    if (!Enum.TryParse<ModoMock>(cambio.Modo, ignoreCase: true, out var modo))
    {
        return Results.Problem(
            title: "Modo invalido",
            detail: $"Modo '{cambio.Modo}' no reconocido. Validos: NORMAL, LENTO, CAIDO, RECHAZO.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var actual = estado.Configuracion;

    // null = no enviado, se conserva; cualquier valor presente (incluido 0) se
    // aplica. Un negativo o un valor fuera de 0..1 se descarta.
    var latenciaMs = cambio.LatenciaMs is int ms && ms >= 0 ? ms : actual.LatenciaMs;
    var probabilidadFallo = cambio.ProbabilidadFallo is >= 0 and <= 1
        ? cambio.ProbabilidadFallo.Value
        : actual.ProbabilidadFallo;

    return Results.Ok(estado.Configurar(modo, latenciaMs, probabilidadFallo));
});

app.MapGet("/admin/modo", (EstadoMock estado) => Results.Ok(estado.Configuracion));

app.MapGet("/admin/estado", (EstadoMock estado) =>
{
    var configuracion = estado.Configuracion;
    return Results.Ok(new EstadoAdminDto(
        configuracion.Modo,
        configuracion.LatenciaMs,
        configuracion.ProbabilidadFallo,
        estado.Idempotencia.Cantidad,
        estado.Idempotencia.Claves));
});

app.MapGet("/admin/certificaciones", (EstadoMock estado) => Results.Ok(estado.Certificaciones));

app.Run();

static string GenerarFolio() =>
    string.Concat("FOL-", Guid.NewGuid().ToString("N")[..12]).ToUpperInvariant();

static string GenerarAutorizacion() =>
    Guid.NewGuid().ToString("D").ToUpperInvariant();
