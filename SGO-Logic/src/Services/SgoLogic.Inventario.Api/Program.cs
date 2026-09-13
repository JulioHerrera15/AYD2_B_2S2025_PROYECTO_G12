using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Consumers;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;
using SgoLogic.Inventario.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<InventarioDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<InventarioDbContext>(builder.Configuration);

builder.Services.Configure<InventarioOptions>(builder.Configuration.GetSection(InventarioOptions.Seccion));

builder.Services.AddScoped<IRepositorioExistencias, RepositorioExistencias>();
builder.Services.AddScoped<IRepositorioReservas, RepositorioReservas>();
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<IGeneradorCorrelativoReserva, GeneradorCorrelativoReserva>();
builder.Services.AddScoped<ServicioReservas>();
builder.Services.AddScoped<ServicioExistencias>();
builder.Services.AddScoped<ServicioExpiracionReservas>();

builder.Services.AddHostedService<ReservaSolicitadaConsumidor>();

// HU-002: barrido periodico que libera las reservas con TTL vencido.
builder.Services.AddHostedService<ExpiracionReservasWorker>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var inventario = app.MapGroup("/api/inventario");

inventario.MapPost("/reservas", async (
    CrearReservaRequest solicitud,
    ServicioReservas servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
{
    try
    {
        var resultado = await servicio.ReservarAsync(solicitud.ASolicitud(), cancelacion);

        // Un unico SaveChanges: existencias tocadas + reserva + fila de outbox en
        // la misma transaccion. Traduce el choque de version optimista a 409.
        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        if (resultado.Rechazada)
        {
            return Results.Problem(
                title: "Reserva rechazada por inventario",
                detail: "No hay stock suficiente en ninguna ubicacion elegible para la orden.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["codigo"] = "stock_insuficiente",
                    ["motivo"] = resultado.MotivoRechazo?.ToString(),
                    ["lineasFaltantes"] = resultado.LineasFaltantes
                });
        }

        return resultado.YaExistia
            ? Results.Ok(resultado.Reserva)
            : Results.Created($"/api/inventario/reservas/{resultado.Reserva!.OrdenId}", resultado.Reserva);
    }
    catch (Exception ex) when (ex is ExcepcionConflictoConcurrencia)
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
});

inventario.MapGet("/reservas/{ordenId}", async (
    string ordenId,
    ServicioReservas servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        return Results.Ok(await servicio.ObtenerPorOrdenAsync(ordenId, cancelacion));
    }
    catch (ExcepcionReservaNoEncontrada ex)
    {
        return MapearError(ex);
    }
});

inventario.MapPut("/existencias", async (
    RegistrarExistenciaRequest solicitud,
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        return Results.Ok(await servicio.RegistrarAsync(solicitud, cancelacion));
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
});

inventario.MapGet("/existencias/{sku}", async (
    string sku,
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarPorSkuAsync(sku, cancelacion)));

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<InventarioDbContext>();
    contexto.Database.Migrate();
}

app.Run();

static IResult MapearError(Exception ex) => ex switch
{
    ExcepcionReservaNoEncontrada => Results.Problem(
        title: "Reserva no encontrada",
        detail: ex.Message,
        statusCode: StatusCodes.Status404NotFound),

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
