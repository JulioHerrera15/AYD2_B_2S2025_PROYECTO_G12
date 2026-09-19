using System.Text.Json.Serialization;
using SgoLogic.Mocks.LogiExpress;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    opciones.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<EstadoMock>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

// Emision de guia. La solicitud a la que le toca timeout no responde: se queda
// colgada hasta que el cliente se canse. Es a proposito — Despacho debe tratar el
// timeout como resultado de negocio (guia pendiente), no como un 500 que reintentar,
// y para eso el mock tiene que comportarse como un proveedor que no contesta.
app.MapPost("/api/logiexpress/guias", async (
    PeticionGuia peticion,
    EstadoMock estado,
    CancellationToken cancelacion) =>
{
    var recibidoEn = DateTime.UtcNow;
    var configuracion = estado.Configuracion;

    if (estado.LeTocaTimeout())
    {
        estado.RegistrarIntento(new IntentoRegistrado(peticion.OrdenId, "TIMEOUT", null, recibidoEn));

        // Mas que cualquier timeout razonable del cliente.
        await Task.Delay(TimeSpan.FromMinutes(5), cancelacion);
        return Results.Problem(
            title: "LogiExpress no respondio",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }

    if (configuracion.Modo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs, cancelacion);
    }

    var guiaId = $"LE-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    estado.RegistrarGuia(new GuiaRegistrada(
        guiaId, peticion.OrdenId, peticion.UbicacionOrigenId, "GUIA_EMITIDA", recibidoEn));

    estado.RegistrarIntento(new IntentoRegistrado(peticion.OrdenId, "EMITIDA", guiaId, recibidoEn));

    return Results.Ok(new RespuestaGuia(
        guiaId,
        $"https://rastreo.logiexpress.gt/{guiaId}",
        "LogiExpress",
        recibidoEn));
});

// Rastreo de una guia ya emitida.
app.MapGet("/api/logiexpress/guias/{guiaId}", (string guiaId, EstadoMock estado) =>
{
    var guia = estado.BuscarGuia(guiaId);

    return guia is null
        ? Results.Problem(
            title: "Guia no encontrada",
            detail: $"No hay guia con id '{guiaId}'.",
            statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(new RespuestaRastreo(
            guia.GuiaId, guia.OrdenId, guia.Estado, null, DateTime.UtcNow));
});

// Anulacion: la usa Despacho cuando el cliente cambia a recogida en tienda y la
// guia ya estaba emitida.
app.MapDelete("/api/logiexpress/guias/{guiaId}", (string guiaId, EstadoMock estado) =>
    estado.CambiarEstado(guiaId, "ANULADA") is null
        ? Results.Problem(
            title: "Guia no encontrada",
            detail: $"No hay guia con id '{guiaId}'.",
            statusCode: StatusCodes.Status404NotFound)
        : Results.NoContent());

// Empuja un estado de entrega hacia Despacho (HU-012). En un proveedor real esto
// lo dispara el transportista; aqui lo dispara quien corre la demo.
app.MapPost("/admin/guias/{guiaId}/estado", (string guiaId, CambioEstadoGuia cambio, EstadoMock estado) =>
    estado.CambiarEstado(guiaId, cambio.Estado) is { } guia
        ? Results.Ok(new RespuestaRastreo(guia.GuiaId, guia.OrdenId, guia.Estado, cambio.Detalle, DateTime.UtcNow))
        : Results.Problem(
            title: "Guia no encontrada",
            detail: $"No hay guia con id '{guiaId}'.",
            statusCode: StatusCodes.Status404NotFound));

app.MapPut("/admin/modo", (CambioModo cambio, EstadoMock estado) =>
{
    if (!Enum.TryParse<ModoMock>(cambio.Modo, ignoreCase: true, out var modo))
    {
        return Results.Problem(
            title: "Modo invalido",
            detail: $"Modo '{cambio.Modo}' no reconocido. Validos: NORMAL, LENTO, CAIDO.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var actual = estado.Configuracion;
    var latenciaMs = cambio.LatenciaMs is int ms && ms >= 0 ? ms : actual.LatenciaMs;
    var unoDeCada = cambio.UnoDeCada is int n && n >= 0 ? n : actual.UnoDeCada;

    return Results.Ok(estado.Configurar(modo, latenciaMs, unoDeCada));
});

app.MapGet("/admin/modo", (EstadoMock estado) => Results.Ok(estado.Configuracion));

app.MapGet("/admin/guias", (EstadoMock estado) => Results.Ok(estado.Guias));

app.MapGet("/admin/intentos", (EstadoMock estado) => Results.Ok(estado.Intentos));

app.Run();
