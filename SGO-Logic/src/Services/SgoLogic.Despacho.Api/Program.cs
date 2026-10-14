using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Despacho.Api.Application.Despacho;
using SgoLogic.Despacho.Api.Consumers;
using SgoLogic.Despacho.Api.Domain;
using SgoLogic.Despacho.Api.Infrastructure.Inventario;
using SgoLogic.Despacho.Api.Infrastructure.LogiExpress;
using SgoLogic.Despacho.Api.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<DespachoDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<DespachoDbContext>(builder.Configuration);

builder.Services.Configure<DespachoOptions>(builder.Configuration.GetSection(DespachoOptions.Seccion));

builder.Services.AddScoped<IRepositorioDespachos, RepositorioDespachos>();
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<IGeneradorCorrelativoDespacho, GeneradorCorrelativoDespacho>();
builder.Services.AddScoped<ServicioDespacho>();

// Dos clientes HTTP con tolerancias distintas a proposito: Inventario es un
// servicio propio (si no responde, se reintenta por la cola) y LogiExpress es un
// proveedor externo cuyo timeout es un resultado de negocio valido.
builder.Services.AddHttpClient<IClienteInventario, ClienteInventarioHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<DespachoOptions>>().Value;
    http.BaseAddress = new Uri(builder.Configuration["Inventario:BaseUrl"] ?? "http://inventario-api:8080");
    http.Timeout = TimeSpan.FromMilliseconds(opciones.InventarioTimeoutMs);
});

builder.Services.AddHttpClient<IClienteLogiExpress, ClienteLogiExpressHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<DespachoOptions>>().Value;
    http.BaseAddress = new Uri(builder.Configuration["LogiExpress:BaseUrl"] ?? "http://logiexpress-mock:8080");
    http.Timeout = TimeSpan.FromMilliseconds(opciones.LogiExpressTimeoutMs);
});

builder.Services.AddHostedService<OrdenesEventosConsumidor>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var despacho = app.MapGroup("/api/despacho");

despacho.MapGet("/{ordenId}", async (
    string ordenId,
    ServicioDespacho servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        return Results.Ok(await servicio.ObtenerPorOrdenAsync(ordenId, cancelacion));
    }
    catch (ExcepcionDespachoNoEncontrado ex)
    {
        return MapearError(ex);
    }
});

// HU-012: webhook por el que LogiExpress informa el avance de la entrega.
despacho.MapPost("/{ordenId}/estados", async (
    string ordenId,
    ActualizarEstadoRequest solicitud,
    ServicioDespacho servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
{
    if (!Enum.TryParse<EstadoDespacho>(solicitud.Estado, ignoreCase: true, out var estado))
    {
        return Results.Problem(
            title: "Estado invalido",
            detail: $"Estado '{solicitud.Estado}' no reconocido.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var resultado = await servicio.ActualizarEstadoAsync(
            ordenId, estado, solicitud.Detalle, solicitud.OcurridoEn ?? DateTime.UtcNow, cancelacion);

        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(resultado);
    }
    catch (Exception ex) when (ex is ExcepcionDespachoNoEncontrado or ExcepcionTransicionDespachoInvalida
        or ExcepcionDespachoCancelado or ExcepcionConflictoConcurrencia)
    {
        return MapearError(ex);
    }
});

// HU-019: cola de preparacion del operador de una ubicacion.
despacho.MapGet("/ubicaciones/{ubicacionId}/preparacion", async (
    string ubicacionId,
    string? estado,
    ServicioDespacho servicio,
    CancellationToken cancelacion) =>
{
    if (!FiltroEstadoDespacho.IntentarParsear(estado, out var filtro))
    {
        return EstadoInvalido(estado);
    }

    return Results.Ok(await servicio.ListarPorUbicacionOrigenAsync(ubicacionId, filtro, cancelacion));
});

// Panel de despacho: todas las ubicaciones de origen, o una sola con ubicacionId.
// El segmento literal tiene precedencia sobre /{ordenId}, asi que no choca con la
// consulta por orden.
despacho.MapGet("/preparacion", async (
    string? estado,
    string? ubicacionId,
    ServicioDespacho servicio,
    CancellationToken cancelacion) =>
{
    if (!FiltroEstadoDespacho.IntentarParsear(estado, out var filtro))
    {
        return EstadoInvalido(estado);
    }

    return Results.Ok(await servicio.ListarAsync(ubicacionId, filtro, cancelacion));
});

// HU-011: reasignacion del metodo de entrega. Tambien llega por el evento
// ordenes.orden.metodo-entrega-modificado; se expone por REST para poder ejercerlo
// sin depender de que Ordenes lo emita.
despacho.MapPut("/{ordenId}/metodo-entrega", async (
    string ordenId,
    CambioMetodoRequest solicitud,
    ServicioDespacho servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
{
    if (!Enum.TryParse<MetodoEntrega>(solicitud.MetodoEntrega, ignoreCase: true, out var metodo))
    {
        return Results.Problem(
            title: "Metodo de entrega invalido",
            detail: $"Metodo '{solicitud.MetodoEntrega}' no reconocido.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var resultado = await servicio.CambiarMetodoEntregaAsync(
            new SolicitudCambioMetodo(ordenId, metodo, solicitud.UbicacionRecogidaId, solicitud.DireccionEntrega),
            cancelacion);

        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(resultado);
    }
    catch (Exception ex) when (ex is ExcepcionDespachoNoEncontrado or ExcepcionTransicionDespachoInvalida or ExcepcionConflictoConcurrencia)
    {
        return MapearError(ex);
    }
});

// Alta directa, para probar sin depender de que Ordenes emita el evento.
despacho.MapPost("/", async (
    SolicitudDespacho solicitud,
    ServicioDespacho servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.SolicitarAsync(solicitud, cancelacion);
        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(resultado);
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (ExcepcionConflictoConcurrencia ex)
    {
        return MapearError(ex);
    }
});

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<DespachoDbContext>();
    contexto.Database.Migrate();
}

app.Run();

static IResult EstadoInvalido(string? estado) => Results.Problem(
    title: "Estado invalido",
    detail: $"Estado '{estado}' no reconocido.",
    statusCode: StatusCodes.Status400BadRequest);

static IResult MapearError(Exception ex) => ex switch
{
    ExcepcionDespachoNoEncontrado => Results.Problem(
        title: "Despacho no encontrado",
        detail: ex.Message,
        statusCode: StatusCodes.Status404NotFound),

    ExcepcionTransicionDespachoInvalida => Results.Problem(
        title: "Transicion invalida",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "transicion_invalida" }),

    ExcepcionDespachoCancelado => Results.Problem(
        title: "El despacho fue cancelado",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "despacho_cancelado" }),

    ExcepcionConflictoConcurrencia => Results.Problem(
        title: "Conflicto de concurrencia",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "conflicto_concurrencia" }),

    _ => Results.Problem(
        title: "Error interno",
        detail: ex.Message,
        statusCode: StatusCodes.Status500InternalServerError)
};
