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

builder.Services.AddSingleton<EstadoMock>();
builder.Services.Configure<BovedaOptions>(builder.Configuration.GetSection(BovedaOptions.Seccion));
builder.Services.AddSingleton(proveedor => new BovedaTarjetas(
    () => DateTime.UtcNow,
    proveedor.GetRequiredService<IOptions<BovedaOptions>>().Value.RutaArchivo));

var app = builder.Build();

// Este mock no registra cuerpos de peticion: no hay UseHttpLogging ni middleware de
// logging de request, y ningun handler escribe la peticion de cargo en un log. El
// numero de tarjeta y el CVV solo existen en la variable local de POST /v1/cargos,
// mientras se validan; la boveda conserva unicamente ultimos cuatro, marca y
// vencimiento.
//
// Ya no hay tokenizacion desde el navegador (POST /v1/tokens) ni tokens desde la
// boveda (POST /v1/tarjetas/tokens): la tokenizacion la hace Pagos con su boveda
// transitoria, y PayKlik recibe los datos solo al cobrar, servidor a servidor.

app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

// Inspeccion de la demo: tarjetas de la boveda. Solo referencia, marca, ultimos
// cuatro y vencimiento; la boveda no tiene PAN ni CVV.
app.MapGet("/admin/tarjetas", (BovedaTarjetas boveda) => Results.Ok(boveda.Listar()));

// Cobro. Para TARJETA recibe los datos de una tarjeta nueva ({ tarjeta, guardar }) o
// la referencia de una guardada con su CVV ({ referenciaTarjeta, cvv }). Datos que no
// pasan la validacion responden 422 con un campo "codigo" (TARJETA_INVALIDA,
// CVV_INVALIDO o TARJETA_VENCIDA) y una referencia inexistente 404
// (TARJETA_NO_ENCONTRADA), distinguibles del rechazo por fondos, que es 200 con
// estado RECHAZADO. Nunca devuelve el numero ni el CVV.
app.MapPost("/v1/cargos", async (
    PeticionCargo peticion,
    EstadoMock estado,
    BovedaTarjetas boveda,
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

    var modoEfectivo = ResolverModoEfectivo(configuracion);

    // latenciaMs igual a 0 se aplica como cero: no hay espera.
    if (modoEfectivo == ModoMock.LENTO && configuracion.LatenciaMs > 0)
    {
        await Task.Delay(configuracion.LatenciaMs);
    }

    // CAIDO sale antes de mirar la tarjeta: no procesa nada ni guarda nada.
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

    ResumenTarjeta? tarjeta = null;
    if (string.Equals(peticion.MedioPago, "TARJETA", StringComparison.OrdinalIgnoreCase))
    {
        var (resumen, rechazo, codigo) = ResolverTarjeta(peticion, boveda);
        if (rechazo is not null)
        {
            estado.Registrar(new CargoRegistrado(
                peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
                Resultado: codigo!, AutorizacionId: null, RecibidoEn: recibidoEn));
            return rechazo;
        }

        tarjeta = resumen;
    }

    var aprobado = modoEfectivo != ModoMock.RECHAZO;

    // La tarjeta nueva se guarda en la boveda solo si el cargo se aprobo.
    string? referenciaGuardada = null;
    if (aprobado && tarjeta is not null && peticion.Tarjeta is not null && peticion.Guardar == true)
    {
        referenciaGuardada = boveda.Guardar(tarjeta);
    }

    var respuesta = new RespuestaCargo(
        AutorizacionId: aprobado ? GenerarAutorizacionId() : null,
        Estado: aprobado ? "APROBADO" : "RECHAZADO",
        CodigoRespuesta: aprobado ? "00" : "51",
        Mensaje: aprobado ? "Cargo aprobado" : "Fondos insuficientes",
        UltimosCuatro: tarjeta?.UltimosCuatro,
        Marca: tarjeta?.Marca,
        ProcesadoEn: recibidoEn,
        ReferenciaTarjeta: referenciaGuardada);

    // Solo se guarda para idempotencia una respuesta efectivamente procesada (200).
    // Si dos peticiones con la misma clave llegaron a la vez, ambas devuelven la
    // primera que quedo guardada.
    if (!string.IsNullOrWhiteSpace(claveIdempotencia))
    {
        respuesta = estado.Idempotencia.ObtenerOAgregar(claveIdempotencia, () => respuesta);
    }

    estado.Registrar(new CargoRegistrado(
        peticion.Referencia, peticion.Monto, peticion.Moneda, peticion.MedioPago,
        Resultado: respuesta.Estado, AutorizacionId: respuesta.AutorizacionId, RecibidoEn: recibidoEn));

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

// Tarjeta de un cargo con TARJETA: valida los datos de una tarjeta nueva o la
// referencia + CVV de una guardada. Devuelve el resumen (ultimos cuatro, marca) o
// la respuesta de rechazo con su codigo.
static (ResumenTarjeta? Tarjeta, IResult? Rechazo, string? Codigo) ResolverTarjeta(PeticionCargo peticion, BovedaTarjetas boveda)
{
    if (peticion.Tarjeta is not null)
    {
        var validacion = ValidadorTarjeta.Validar(peticion.Tarjeta, DateTime.UtcNow);
        if (validacion.EsValida)
        {
            return (validacion.Tarjeta, null, null);
        }

        return (null, Rechazo(
            StatusCodes.Status422UnprocessableEntity,
            "Tarjeta invalida",
            "Los datos de la tarjeta no superaron la validacion.",
            "TARJETA_INVALIDA",
            validacion.Errores), "TARJETA_INVALIDA");
    }

    if (!string.IsNullOrWhiteSpace(peticion.ReferenciaTarjeta))
    {
        var (resultado, guardada) = boveda.Validar(peticion.ReferenciaTarjeta, peticion.Cvv);

        return resultado switch
        {
            ResultadoValidacionReferencia.Valida => (guardada!.AResumen(), null, null),
            ResultadoValidacionReferencia.NoExiste => (null, Rechazo(
                StatusCodes.Status404NotFound,
                "Tarjeta no encontrada",
                "La referencia de tarjeta no existe en la boveda.",
                "TARJETA_NO_ENCONTRADA"), "TARJETA_NO_ENCONTRADA"),
            ResultadoValidacionReferencia.CvvInvalido => (null, Rechazo(
                StatusCodes.Status422UnprocessableEntity,
                "Codigo de seguridad invalido",
                "El codigo de seguridad no es valido para esta tarjeta.",
                "CVV_INVALIDO",
                new[] { new ErrorValidacionTarjeta("cvv", guardada!.Marca == "AMEX" ? "El CVV debe tener 4 digitos." : "El CVV debe tener 3 digitos.") }), "CVV_INVALIDO"),
            _ => (null, Rechazo(
                StatusCodes.Status422UnprocessableEntity,
                "Tarjeta vencida",
                "La tarjeta guardada esta vencida.",
                "TARJETA_VENCIDA",
                new[] { new ErrorValidacionTarjeta("expiracion", "La tarjeta esta vencida.") }), "TARJETA_VENCIDA")
        };
    }

    return (null, Rechazo(
        StatusCodes.Status422UnprocessableEntity,
        "Datos de tarjeta requeridos",
        "El cargo con TARJETA requiere los datos de la tarjeta o la referencia de una tarjeta guardada.",
        "TARJETA_INVALIDA"), "TARJETA_INVALIDA");
}

static IResult Rechazo(int status, string titulo, string detalle, string codigo, IReadOnlyList<ErrorValidacionTarjeta>? errores = null)
{
    var extensiones = new Dictionary<string, object?> { ["codigo"] = codigo };
    if (errores is not null)
    {
        extensiones["errores"] = errores;
    }

    return Results.Problem(title: titulo, detail: detalle, statusCode: status, extensions: extensiones);
}
