using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SgoLogic.Mocks.PayKlik;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    opciones.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    opciones.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<TokenizacionOptions>(builder.Configuration.GetSection(TokenizacionOptions.Seccion));
builder.Services.AddSingleton<EstadoMock>();
builder.Services.Configure<BovedaOptions>(builder.Configuration.GetSection(BovedaOptions.Seccion));
builder.Services.AddSingleton(proveedor => new BovedaTarjetas(
    () => DateTime.UtcNow,
    proveedor.GetRequiredService<IOptions<BovedaOptions>>().Value.RutaArchivo));

// Decision del mock para la demo: POST /v1/tokens acepta cualquier origen porque el
// navegador lo llama directo desde la UI (asi el numero de tarjeta y el CVV viajan
// del navegador a PayKlik sin tocar ningun servicio de SGO-Logic). Un proveedor real
// restringiria los origenes permitidos. El resto de endpoints del mock no llevan CORS.
const string PoliticaCorsTokens = "TokenizacionAbierta";
builder.Services.AddCors(opciones => opciones.AddPolicy(PoliticaCorsTokens, politica => politica
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .WithMethods("POST")
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

var app = builder.Build();

// Este mock no registra cuerpos de peticion: no hay UseHttpLogging ni middleware de
// logging de request, y ningun handler escribe la peticion de tokenizacion en un log.
// El numero de tarjeta y el CVV solo existen en la variable local de POST /v1/tokens,
// mientras se validan; el almacen conserva unicamente ultimos cuatro y marca.
app.UseCors();

app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

// Tokenizacion. Recibe los datos de la tarjeta, los valida y devuelve un token
// opaco de un solo uso. Es el unico endpoint que ve un PAN o un CVV.
app.MapPost("/v1/tokens", async (
    PeticionTokenizacion peticion,
    EstadoMock estado,
    BovedaTarjetas boveda,
    IOptions<TokenizacionOptions> opciones) =>
{
    var configuracion = estado.Configuracion;
    var modoEfectivo = ResolverModoEfectivo(configuracion);

    // latenciaMs igual a 0 se aplica como cero: no hay espera.
    if (modoEfectivo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs);
    }

    if (modoEfectivo == ModoMock.CAIDO)
    {
        return Results.Problem(
            title: "PayKlik no disponible",
            detail: "El proveedor no tokenizo la tarjeta (modo CAIDO).",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var validacion = ValidadorTarjeta.Validar(peticion, DateTime.UtcNow);
    if (!validacion.EsValida)
    {
        return Results.Problem(
            title: "Tarjeta invalida",
            detail: "Los datos de la tarjeta no superaron la validacion.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?>
            {
                ["codigo"] = "TARJETA_INVALIDA",
                ["errores"] = validacion.Errores
            });
    }

    var vigencia = TimeSpan.FromSeconds(Math.Max(1, opciones.Value.VigenciaSegundos));
    var respuesta = estado.Tokens.Emitir(validacion.Tarjeta!, vigencia);

    // guardar=true: ademas del token de un solo uso, la tarjeta queda en la boveda
    // con una referencia persistente. La boveda nunca recibe el PAN ni el CVV.
    if (peticion.Guardar == true)
    {
        respuesta = respuesta with { ReferenciaTarjeta = boveda.Guardar(validacion.Tarjeta!) };
    }

    return Results.Json(respuesta, statusCode: StatusCodes.Status201Created);
}).RequireCors(PoliticaCorsTokens);

// Token de un solo uso a partir de una tarjeta guardada en la boveda. Lo llama
// Pagos (servidor a servidor) con la referencia y el CVV que el cliente escribio;
// el CVV se valida (4 digitos AMEX, 3 las demas) y se descarta. Devuelve la misma
// forma que POST /v1/tokens. 404 si la referencia no existe, 422 si el CVV no es
// valido o la tarjeta esta vencida.
app.MapPost("/v1/tarjetas/tokens", async (
    PeticionTokenDesdeReferencia peticion,
    EstadoMock estado,
    BovedaTarjetas boveda,
    IOptions<TokenizacionOptions> opciones) =>
{
    var configuracion = estado.Configuracion;
    var modoEfectivo = ResolverModoEfectivo(configuracion);

    if (modoEfectivo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs);
    }

    if (modoEfectivo == ModoMock.CAIDO)
    {
        return Results.Problem(
            title: "PayKlik no disponible",
            detail: "El proveedor no tokenizo la tarjeta guardada (modo CAIDO).",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var (resultado, tarjeta) = boveda.Validar(peticion);

    switch (resultado)
    {
        case ResultadoTokenDesdeReferencia.NoExiste:
            return Results.Problem(
                title: "Tarjeta no encontrada",
                detail: "La referencia de tarjeta no existe en la boveda.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["codigo"] = "TARJETA_NO_ENCONTRADA" });

        case ResultadoTokenDesdeReferencia.CvvInvalido:
            return Results.Problem(
                title: "Tarjeta invalida",
                detail: "El codigo de seguridad no es valido para esta tarjeta.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["codigo"] = "CVV_INVALIDO",
                    ["errores"] = new[]
                    {
                        new ErrorValidacionTarjeta(
                            "cvv",
                            tarjeta!.Marca == "AMEX" ? "El CVV debe tener 4 digitos." : "El CVV debe tener 3 digitos.")
                    }
                });

        case ResultadoTokenDesdeReferencia.Vencida:
            return Results.Problem(
                title: "Tarjeta vencida",
                detail: "La tarjeta guardada esta vencida.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["codigo"] = "TARJETA_VENCIDA",
                    ["errores"] = new[] { new ErrorValidacionTarjeta("expiracion", "La tarjeta esta vencida.") }
                });
    }

    var vigencia = TimeSpan.FromSeconds(Math.Max(1, opciones.Value.VigenciaSegundos));
    return Results.Json(
        estado.Tokens.Emitir(tarjeta!.ATarjetaTokenizada(), vigencia),
        statusCode: StatusCodes.Status201Created);
});

// Inspeccion de la demo: tarjetas de la boveda. Solo referencia, marca, ultimos
// cuatro y vencimiento; la boveda no tiene PAN ni CVV.
app.MapGet("/admin/tarjetas", (BovedaTarjetas boveda) => Results.Ok(boveda.Listar()));

// Inspeccion de la demo: solo token, ultimos cuatro, marca y vigencia. Nunca el PAN.
app.MapGet("/admin/tokens", (EstadoMock estado) => Results.Ok(estado.Tokens.Listar()));

// Cobro. Nunca recibe ni devuelve numero de tarjeta ni CVV: para TARJETA recibe el
// token de POST /v1/tokens, lo resuelve contra el almacen en memoria y devuelve los
// ultimos cuatro y la marca reales. Un token inexistente, vencido o ya usado responde
// 422 con un campo "codigo" (TOKEN_INVALIDO, TOKEN_VENCIDO, TOKEN_YA_USADO o
// TOKEN_REQUERIDO), distinguible del rechazo por fondos, que es 200 con estado
// RECHAZADO.
app.MapPost("/v1/cargos", async (
    PeticionCargo peticion,
    EstadoMock estado,
    [FromHeader(Name = "Idempotency-Key")] string? claveIdempotencia) =>
{
    var configuracion = estado.Configuracion;
    var recibidoEn = DateTime.UtcNow;

    // Idempotencia: ante una clave ya vista se devuelve la misma respuesta sin
    // volver a procesar el cargo. Va antes de resolver el token: un reintento del
    // mismo cargo llega con un token que la primera llamada ya consumio.
    if (!string.IsNullOrWhiteSpace(claveIdempotencia) &&
        estado.Idempotencia.TryObtener(claveIdempotencia, out var previa))
    {
        estado.Registrar(new CargoRegistrado(
            peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
            Resultado: "IDEMPOTENTE", AutorizacionId: previa.AutorizacionId, RecibidoEn: recibidoEn));

        return Results.Ok(previa);
    }

    var modoEfectivo = ResolverModoEfectivo(configuracion);

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

    // Solo un cargo procesado consume el token: un 503 (CAIDO) sale antes y lo deja
    // intacto para el reintento.
    TarjetaTokenizada? tarjeta = null;
    if (string.Equals(peticion.MedioPago, "TARJETA", StringComparison.OrdinalIgnoreCase))
    {
        var consumo = estado.Tokens.Consumir(peticion.TokenMedioPago);

        if (consumo.Resultado != ResultadoConsumoToken.Consumido)
        {
            // Dos peticiones concurrentes con la misma clave: la perdedora ve el token
            // ya consumido, pero corresponde devolverle la respuesta de la ganadora.
            if (!string.IsNullOrWhiteSpace(claveIdempotencia) &&
                estado.Idempotencia.TryObtener(claveIdempotencia, out var ganadora))
            {
                return Results.Ok(ganadora);
            }

            var codigo = string.IsNullOrWhiteSpace(peticion.TokenMedioPago)
                ? "TOKEN_REQUERIDO"
                : consumo.Resultado switch
                {
                    ResultadoConsumoToken.Vencido => "TOKEN_VENCIDO",
                    ResultadoConsumoToken.YaUsado => "TOKEN_YA_USADO",
                    _ => "TOKEN_INVALIDO"
                };

            estado.Registrar(new CargoRegistrado(
                peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
                Resultado: codigo, AutorizacionId: null, RecibidoEn: recibidoEn));

            return Results.Problem(
                title: "Token de medio de pago no valido",
                detail: DetalleTokenNoValido(codigo),
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["codigo"] = codigo });
        }

        tarjeta = consumo.Tarjeta;
    }

    var aprobado = modoEfectivo != ModoMock.RECHAZO;

    var respuesta = new RespuestaCargo(
        AutorizacionId: aprobado ? GenerarAutorizacionId() : null,
        Estado: aprobado ? "APROBADO" : "RECHAZADO",
        CodigoRespuesta: aprobado ? "00" : "51",
        Mensaje: aprobado ? "Cargo aprobado" : "Fondos insuficientes",
        UltimosCuatro: tarjeta?.UltimosCuatro,
        Marca: tarjeta?.Marca,
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

    var modoEfectivo = ResolverModoEfectivo(configuracion);

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

// INTERMITENTE se resuelve por peticion: falla como CAIDO con la probabilidad
// configurada y se comporta como NORMAL el resto de las veces. Los demas modos
// se devuelven tal cual.
static ModoMock ResolverModoEfectivo(ConfiguracionMock configuracion)
{
    if (configuracion.Modo != ModoMock.INTERMITENTE)
    {
        return configuracion.Modo;
    }

    return Random.Shared.NextDouble() < configuracion.ProbabilidadFallo
        ? ModoMock.CAIDO
        : ModoMock.NORMAL;
}

static string DetalleTokenNoValido(string codigo) => codigo switch
{
    "TOKEN_REQUERIDO" => "El cargo con TARJETA requiere un token de medio de pago.",
    "TOKEN_VENCIDO" => "El token de medio de pago vencio; hay que tokenizar la tarjeta de nuevo.",
    "TOKEN_YA_USADO" => "El token de medio de pago ya se uso en otro cargo; son de un solo uso.",
    _ => "El token de medio de pago no existe."
};
