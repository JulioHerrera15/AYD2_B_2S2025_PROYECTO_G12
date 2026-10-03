using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Consumers;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Api.Infrastructure.Carrito;
using SgoLogic.Ordenes.Api.Infrastructure.Inventario;
using SgoLogic.Ordenes.Api.Infrastructure.Persistencia;
using SgoLogic.Ordenes.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<OrdenesDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<OrdenesDbContext>(builder.Configuration);

builder.Services.Configure<MediosPagoPorCanalOptions>(
    builder.Configuration.GetSection(MediosPagoPorCanalOptions.Seccion));
builder.Services.AddSingleton<PoliticaMediosPago>();

// Tasa del IVA incluido en los precios: Ordenes calcula total e impuestos en el servidor.
builder.Services.Configure<FiscalOptions>(builder.Configuration.GetSection(FiscalOptions.Seccion));

builder.Services.AddScoped<IRepositorioOrdenes, RepositorioOrdenes>();
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<IGeneradorCorrelativoOrden, GeneradorCorrelativoOrden>();
builder.Services.AddScoped<IGeneradorCorrelativoDevolucion, GeneradorCorrelativoDevolucion>();
builder.Services.AddScoped<ServicioConfirmacionOrden>();
builder.Services.AddScoped<ServicioDevolucion>();

// Llamada directa a Auth para vaciar el carrito al confirmar una orden (mismo
// patron que ClienteInventarioHttp de Despacho). Sin circuit breaker: al
// contrario que ese caso, aca un fallo se traga en el propio cliente (ver
// ClienteCarritoHttp) porque vaciar el carrito es una cortesia, no algo de lo
// que dependa confirmar la orden.
builder.Services.AddHttpClient<IClienteCarrito, ClienteCarritoHttp>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Auth:BaseUrl"] ?? "http://auth-api:8080");
    http.Timeout = TimeSpan.FromSeconds(5);
});

// B1: precios de la orden contra el catalogo de Inventario (red interna). Cache corta
// por SKU en CatalogoProductosHttp; sin respuesta de Inventario la orden no se crea.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ICatalogoProductos, CatalogoProductosHttp>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Inventario:BaseUrl"] ?? "http://inventario-api:8080");
    http.Timeout = TimeSpan.FromSeconds(3);
});

// Identidad para compras a credito: misma validacion de JWT que Pagos, sin exigirla
// globalmente. Solo POST /api/ordenes con medioPago CREDITO la usa (ver
// ReglaIdentidadCredito); el resto de endpoints y ordenes se comportan igual que antes.
// Sin secreto configurado ningun token valida y toda orden a credito responde 401.
var jwtSecret = builder.Configuration["Auth:JwtSecret"];
var claveJwt = string.IsNullOrWhiteSpace(jwtSecret)
    ? RandomNumberGenerator.GetBytes(32)
    : Encoding.UTF8.GetBytes(jwtSecret);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(claveJwt),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// Consumo de los resultados de Pagos: avance de la saga y compensacion (HU-003).
builder.Services.AddScoped<IRegistroMensajesProcesados, RegistroMensajesProcesados>();
builder.Services.AddScoped<ManejadorEventosPagos>();
builder.Services.AddHostedService<PagosEventosConsumidor>();

// Consumo de los resultados de Inventario: reserva confirmada, rechazada y expirada.
builder.Services.AddScoped<ManejadorEventosInventario>();
builder.Services.AddHostedService<InventarioEventosConsumidor>();

// Consumo de los estados de entrega de Despacho: DESPACHO_SOLICITADO -> EN_PREPARACION ->
// EN_TRANSITO / LISTA_PARA_RECOGER -> ENTREGADA.
builder.Services.AddScoped<ManejadorEventosDespacho>();
builder.Services.AddHostedService<DespachoEventosConsumidor>();

// Consumo del resultado de certificacion fiscal: cierre CONFIRMADA_PENDIENTE_FISCAL → CONFIRMADA (HU-005).
builder.Services.AddScoped<ManejadorDocumentoFiscalCertificado>();
builder.Services.AddScoped<ManejadorCertificacionSatFallida>();
builder.Services.AddHostedService<DocumentoFiscalCertificadoConsumidor>();

// Ruta de credito: respuesta de Credito (aprobado/rechazado) y timeout de la evaluacion.
builder.Services.Configure<CreditoSagaOptions>(builder.Configuration.GetSection(CreditoSagaOptions.Seccion));
builder.Services.AddScoped<ManejadorEventosCredito>();
builder.Services.AddHostedService<CreditoEventosConsumidor>();
builder.Services.AddScoped<ServicioTimeoutCredito>();
builder.Services.AddHostedService<TimeoutEvaluacionCreditoWorker>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

var ordenes = app.MapGroup("/api/ordenes");

ordenes.MapPost("/", async (
    CrearOrdenRequest solicitud,
    HttpContext http,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    // Solo para CREDITO: la linea es del cliente, asi que la orden la crea el propio
    // cliente con sesion. Cualquier otro medio de pago sigue igual que antes.
    switch (ReglaIdentidadCredito.Verificar(solicitud.MedioPago, http.User, solicitud.ClienteId))
    {
        case ResultadoIdentidadCredito.RequiereSesion:
            return Results.Problem(
                title: "Sesion requerida",
                detail: "Para pagar con credito debes iniciar sesion.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["codigo"] = "credito_requiere_sesion" });

        case ResultadoIdentidadCredito.ClienteNoCoincide:
            return Results.Problem(
                title: "Cliente no coincide",
                detail: "El cliente de la orden no coincide con la sesion.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["codigo"] = "cliente_no_coincide" });
    }

    try
    {
        var creada = await servicio.CrearOrdenAsync(solicitud, cancelacion);
        return Results.Created($"/api/ordenes/{creada.Id}", creada);
    }
    catch (ExcepcionMedioPagoNoHabilitado ex)
    {
        return Results.Problem(
            title: "Medio de pago no habilitado",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["codigo"] = "medio_pago_no_habilitado" });
    }
    catch (ExcepcionTokenMedioPagoRequerido ex)
    {
        return Results.Problem(
            title: "Token de medio de pago requerido",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["codigo"] = "token_medio_pago_requerido" });
    }
    catch (ExcepcionPrecioNoValido ex)
    {
        return Results.Problem(
            title: "Precio no valido",
            detail: ex.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?>
            {
                ["codigo"] = "precio_no_valido",
                ["sku"] = ex.Sku,
                ["precioVigente"] = ex.PrecioVigente
            });
    }
    catch (ExcepcionCatalogoNoDisponible ex)
    {
        return Results.Problem(
            title: "Catalogo no disponible",
            detail: "No se pudieron validar los precios con Inventario. Intenta de nuevo en unos momentos.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["codigo"] = "catalogo_no_disponible", ["motivo"] = ex.Message });
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
});

ordenes.MapGet("/medios-pago", (
    Canal canal,
    PoliticaMediosPago politica) =>
    Results.Ok(politica.MediosHabilitados(canal)));

ordenes.MapPost("/{id}/confirmar", async (
    string id,
    ConfirmarOrdenRequest? solicitud,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.ConfirmarOrdenAsync(id, solicitud?.Motivo, cancelacion);
        return Results.Ok(resultado);
    }
    catch (Exception ex) when (
        ex is ExcepcionOrdenNoEncontrada or ExcepcionTransicionInvalida or ExcepcionConflictoConcurrencia)
    {
        return MapearError(ex);
    }
});

ordenes.MapPost("/{id}/metodo-entrega", async (
    string id,
    CambiarMetodoEntregaRequest solicitud,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.CambiarMetodoEntregaAsync(id, solicitud, cancelacion);
        return Results.Ok(resultado);
    }
    catch (Exception ex) when (
        ex is ExcepcionOrdenNoEncontrada or ExcepcionConflictoConcurrencia
        or ArgumentException or InvalidOperationException)
    {
        return MapearError(ex);
    }
});

// Cancelacion por el cliente: libera la reserva por compensacion de la saga.
ordenes.MapPost("/{id}/cancelar", async (
    string id,
    CancelarOrdenRequest solicitud,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.CancelarPorClienteAsync(id, solicitud, cancelacion);
        return Results.Ok(new { id = resultado.Orden.Id, estado = resultado.Orden.Estado });
    }
    catch (ExcepcionOrdenAjena ex)
    {
        return Results.Problem(
            title: "La orden no pertenece al cliente",
            detail: ex.Message,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["codigo"] = "orden_ajena" });
    }
    catch (ExcepcionCancelacionNoPermitida ex)
    {
        return Results.Problem(
            title: ex.Message,
            detail: ex.Message,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["codigo"] = ex.PagoEnCurso ? "pago_en_curso" : "orden_no_cancelable",
                ["estado"] = ex.Estado.ToString()
            });
    }
    catch (Exception ex) when (
        ex is ExcepcionOrdenNoEncontrada or ExcepcionTransicionInvalida or ExcepcionConflictoConcurrencia)
    {
        return MapearError(ex);
    }
});

ordenes.MapGet("/{id}", async (
    string id,
    IRepositorioOrdenes repositorio,
    CancellationToken cancelacion) =>
{
    var orden = await repositorio.ObtenerPorIdAsync(id, cancelacion);
    return orden is null
        ? Results.Problem(
            title: "Orden no encontrada",
            detail: $"No existe la orden {id}.",
            statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(OrdenDto.Desde(orden));
});

ordenes.MapPost("/{id}/reintentar-certificacion", async (
    string id,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.ReintentarCertificacionAsync(id, cancelacion);
        return Results.Ok(resultado);
    }
    catch (Exception ex) when (
        ex is ExcepcionOrdenNoEncontrada or InvalidOperationException)
    {
        return MapearError(ex);
    }
});

ordenes.MapGet("/alertas-fiscales", async (
    IRepositorioOrdenes repositorio,
    CancellationToken cancelacion) =>
{
    var ordenesConAlerta = await repositorio.ObtenerConAlertaFiscalAsync(cancelacion);
    var resultado = ordenesConAlerta.Select(o => new AlertaFiscalDto(
        o.Id,
        o.AlertaFiscalEn!.Value,
        o.AlertaFiscalMotivo ?? string.Empty,
        o.Total)).ToList();
    return Results.Ok(resultado);
});

ordenes.MapPost("/{id}/devoluciones", async (
    string id,
    SolicitudDevolucion solicitud,
    ServicioDevolucion servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var solicitudConId = solicitud with { OrdenId = id };
        var devolucion = await servicio.RegistrarDevolucionAsync(solicitudConId, cancelacion);
        return Results.Created($"/api/ordenes/{id}/devoluciones/{devolucion.Id}", devolucion);
    }
    catch (Exception ex) when (
        ex is ExcepcionOrdenNoEncontrada or ExcepcionTransicionInvalida or ExcepcionConflictoConcurrencia)
    {
        return MapearError(ex);
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (InvalidOperationException ex)
    {
        // Errores de validación de negocio (ej. orden no en ENTREGADA) -> 400
        return Results.Problem(
            title: "Operacion no permitida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["codigo"] = "estado_orden_invalido" });
    }
    catch (DbUpdateException)
    {
        return Results.Problem(
            title: "Error de base de datos",
            detail: "No se pudo persistir la devolucion. Verifique que la migracion se haya aplicado.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

ordenes.MapGet("/{id}/devoluciones", async (
    string id,
    ServicioDevolucion servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var devoluciones = await servicio.ObtenerDevolucionesAsync(id, cancelacion);
        return Results.Ok(devoluciones);
    }
    catch (Exception ex) when (ex is ExcepcionOrdenNoEncontrada)
    {
        return MapearError(ex);
    }
    catch (DbUpdateException)
    {
        return Results.Problem(
            title: "Error de base de datos",
            detail: "No se pudo consultar las devoluciones. Verifique que la migracion se haya aplicado.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<OrdenesDbContext>();
    contexto.Database.Migrate();
}

app.Run();

static IResult MapearError(Exception ex) => ex switch
{
    ExcepcionOrdenNoEncontrada => Results.Problem(
        title: "Orden no encontrada",
        detail: ex.Message,
        statusCode: StatusCodes.Status404NotFound),

    ExcepcionTransicionInvalida => Results.Problem(
        title: "Transicion invalida",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "transicion_invalida" }),

    ExcepcionConflictoConcurrencia => Results.Problem(
        title: "Conflicto de concurrencia",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "conflicto_concurrencia" }),

    ArgumentException => Results.Problem(
        title: "Solicitud invalida",
        detail: ex.Message,
        statusCode: StatusCodes.Status400BadRequest,
        extensions: new Dictionary<string, object?> { ["codigo"] = "metodo_entrega_invalido" }),

    InvalidOperationException => Results.Problem(
        title: "Operacion no permitida",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "conflicto" }),

    _ => Results.Problem(
        title: "Error interno",
        detail: ex.Message,
        statusCode: StatusCodes.Status500InternalServerError)
};

public sealed record AlertaFiscalDto(
    string OrdenId,
    DateTime AlertaFiscalEn,
    string Motivo,
    decimal Monto);
