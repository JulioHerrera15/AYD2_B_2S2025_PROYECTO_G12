using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Mocks.PayKlik;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    opciones.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<EstadoMock>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

// Cobro. Nunca recibe ni devuelve numero de tarjeta ni CVV.
app.MapPost("/v1/cargos", async (
    PeticionCargo peticion,
    EstadoMock estado,
    [FromHeader(Name = "Idempotency-Key")] string? claveIdempotencia) =>
{
    var configuracion = estado.Configuracion;
    var recibidoEn = DateTime.UtcNow;

    // Idempotencia: ante una clave ya vista se devuelve la misma respuesta sin
    // volver a procesar el cargo.
    if (!string.IsNullOrWhiteSpace(claveIdempotencia) &&
        estado.Idempotencia.TryObtener(claveIdempotencia, out var previa))
    {
        estado.Registrar(new CargoRegistrado(
            peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
            Resultado: "IDEMPOTENTE", AutorizacionId: previa.AutorizacionId, RecibidoEn: recibidoEn));

        return Results.Ok(previa);
    }

    var modoEfectivo = configuracion.Modo;
    if (modoEfectivo == ModoMock.INTERMITENTE)
    {
        modoEfectivo = Random.Shared.NextDouble() < configuracion.ProbabilidadFallo
            ? ModoMock.CAIDO
            : ModoMock.NORMAL;
    }

    // latenciaMs igual a 0 se aplica como cero: no hay espera.
    if (modoEfectivo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs);
    }

    if (modoEfectivo == ModoMock.CAIDO)
    {
        estado.Registrar(new CargoRegistrado(
            peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
            Resultado: "NO_PROCESADO", AutorizacionId: null, RecibidoEn: recibidoEn));

        return Results.Problem(
            title: "PayKlik no disponible",
            detail: "El proveedor no proceso el cargo (modo CAIDO).",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var aprobado = modoEfectivo != ModoMock.RECHAZO;
    var (ultimosCuatro, marca) = DerivarTarjeta(peticion.TokenMedioPago);

    var respuesta = new RespuestaCargo(
        AutorizacionId: aprobado ? GenerarAutorizacionId() : null,
        Estado: aprobado ? "APROBADO" : "RECHAZADO",
        CodigoRespuesta: aprobado ? "00" : "51",
        Mensaje: aprobado ? "Cargo aprobado" : "Fondos insuficientes",
        UltimosCuatro: ultimosCuatro,
        Marca: marca,
        ProcesadoEn: recibidoEn);

    estado.Registrar(new CargoRegistrado(
        peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
        Resultado: respuesta.Estado, AutorizacionId: respuesta.AutorizacionId, RecibidoEn: recibidoEn));

    // Solo se guarda para idempotencia una respuesta efectivamente procesada (200).
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
            detail: $"Modo '{cambio.Modo}' no reconocido. Validos: NORMAL, LENTO, CAIDO, RECHAZO, INTERMITENTE.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var actual = estado.Configuracion;

    // null = no enviado, se conserva; cualquier valor presente (incluido 0) se
    // aplica. Un negativo se descarta.
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
        estado.Idempotencia.Cantidad));
});

app.MapGet("/admin/cargos", (EstadoMock estado) => Results.Ok(estado.Cargos));

// Reembolso. Misma estructura que cargos: modos, idempotencia y registro.
app.MapPost("/v1/reembolsos", async (
    PeticionReembolso peticion,
    EstadoMock estado,
    [FromHeader(Name = "Idempotency-Key")] string? claveIdempotencia) =>
{
    var configuracion = estado.Configuracion;
    var recibidoEn = DateTime.UtcNow;

    // Idempotencia: ante una clave ya vista se devuelve la misma respuesta sin
    // volver a procesar el reembolso.
    if (!string.IsNullOrWhiteSpace(claveIdempotencia) &&
        estado.IdempotenciaReembolso.TryObtener(claveIdempotencia, out var previa))
    {
        estado.RegistrarReembolso(new ReembolsoRegistrado(
            peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.AutorizacionOriginal,
            Resultado: "IDEMPOTENTE", AutorizacionId: previa.AutorizacionId, RecibidoEn: recibidoEn));

        return Results.Ok(previa);
    }

    var modoEfectivo = configuracion.Modo;
    if (modoEfectivo == ModoMock.INTERMITENTE)
    {
        modoEfectivo = Random.Shared.NextDouble() < configuracion.ProbabilidadFallo
            ? ModoMock.CAIDO
            : ModoMock.NORMAL;
    }

    // latenciaMs igual a 0 se aplica como cero: no hay espera.
    if (modoEfectivo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs);
    }

    if (modoEfectivo == ModoMock.CAIDO)
    {
        estado.RegistrarReembolso(new ReembolsoRegistrado(
            peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.AutorizacionOriginal,
            Resultado: "NO_PROCESADO", AutorizacionId: null, RecibidoEn: recibidoEn));

        return Results.Problem(
            title: "PayKlik no disponible",
            detail: "El proveedor no proceso el reembolso (modo CAIDO).",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var aprobado = modoEfectivo != ModoMock.RECHAZO;

    var respuesta = new RespuestaReembolso(
        AutorizacionId: aprobado ? GenerarAutorizacionId() : null,
        Estado: aprobado ? "APROBADO" : "RECHAZADO",
        CodigoRespuesta: aprobado ? "00" : "51",
        Mensaje: aprobado ? "Reembolso aprobado" : "Fondos insuficientes",
        ProcesadoEn: recibidoEn);

    estado.RegistrarReembolso(new ReembolsoRegistrado(
        peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.AutorizacionOriginal,
        Resultado: respuesta.Estado, AutorizacionId: respuesta.AutorizacionId, RecibidoEn: recibidoEn));

    // Solo se guarda para idempotencia una respuesta efectivamente procesada (200).
    if (!string.IsNullOrWhiteSpace(claveIdempotencia))
    {
        estado.IdempotenciaReembolso.ObtenerOAgregar(claveIdempotencia, () => respuesta);
    }

    return Results.Ok(respuesta);
});

app.MapGet("/admin/reembolsos", (EstadoMock estado) => Results.Ok(estado.Reembolsos));

app.Run();

static string GenerarAutorizacionId() =>
    string.Concat("PK-", Guid.NewGuid().ToString("N")[..12]).ToUpperInvariant();

// Deriva ultimos cuatro y marca a partir del token opaco, sin exponer datos de
// tarjeta. Si el token no trae digitos suficientes, usa un valor de mock.
static (string UltimosCuatro, string Marca) DerivarTarjeta(string? token)
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return ("4242", "VISA");
    }

    var digitos = new string(token.Where(char.IsDigit).ToArray());
    var ultimosCuatro = digitos.Length >= 4 ? digitos[^4..] : "4242";
    var marca = token.Contains("mc", StringComparison.OrdinalIgnoreCase) ? "MASTERCARD" : "VISA";
    return (ultimosCuatro, marca);
}
