using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<OrdenesDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<OrdenesDbContext>(builder.Configuration);

builder.Services.AddScoped<IRepositorioOrdenes, RepositorioOrdenes>();
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<ServicioConfirmacionOrden>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var ordenes = app.MapGroup("/api/ordenes");

ordenes.MapPost("/", async (
    CrearOrdenRequest solicitud,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var creada = await servicio.CrearOrdenAsync(solicitud, cancelacion);
        return Results.Created($"/api/ordenes/{creada.Id}", creada);
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
});

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

ordenes.MapPost("/{id}/transiciones", async (
    string id,
    TransicionarOrdenRequest solicitud,
    ServicioConfirmacionOrden servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.AplicarTransicionAsync(
            id,
            solicitud.EstadoDestino,
            solicitud.PasoSaga,
            solicitud.Motivo,
            solicitud.EsCompensacion,
            cancelacion);
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
        title: "Cambio de metodo de entrega no permitido",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "metodo_entrega_invalido" }),

    _ => Results.Problem(
        title: "Error interno",
        detail: ex.Message,
        statusCode: StatusCodes.Status500InternalServerError)
};
