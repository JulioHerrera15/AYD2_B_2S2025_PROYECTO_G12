using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.Contracts.Enumeraciones;
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
builder.Services.AddScoped<IRepositorioMovimientos, RepositorioMovimientos>();
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<IGeneradorCorrelativoReserva, GeneradorCorrelativoReserva>();
builder.Services.AddScoped<IRepositorioSucursales, RepositorioSucursales>();
builder.Services.AddScoped<IRepositorioReservasCarrito, RepositorioReservasCarrito>();
builder.Services.AddScoped<ServicioReservas>();
builder.Services.AddScoped<ServicioExistencias>();
builder.Services.AddScoped<ServicioExpiracionReservas>();
builder.Services.AddScoped<ServicioLiberacionReservas>();
builder.Services.AddScoped<ServicioReingresoStock>();
builder.Services.AddScoped<ServicioConsumoEntrega>();
builder.Services.AddScoped<ServicioSucursales>();
builder.Services.AddScoped<ServicioReservasCarrito>();
builder.Services.AddScoped<ServicioExpiracionReservasCarrito>();
builder.Services.AddScoped<ServicioSimulacionReserva>();

builder.Services.AddHostedService<OrdenesEventosConsumidor>();

// HU-002: barrido periodico que libera las reservas con TTL vencido (y las
// retenciones de carrito vencidas, en el mismo ciclo).
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

// Desde donde saldria el pedido si se reservara ahora, sin reservar nada. Cuenta
// como disponibles las retenciones activas del carrito indicado. No escribe.
inventario.MapPost("/reservas/simular", async (
    SimularReservaRequest solicitud,
    ServicioSimulacionReserva servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        return Results.Ok(await servicio.SimularAsync(solicitud, cancelacion));
    }
    catch (ArgumentException ex)
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

inventario.MapPost("/reservas/{ordenId}/consumir", async (
    string ordenId,
    ServicioConsumoEntrega servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
{
    try
    {
        // Misma logica que el consumidor de despacho: solo consume reservas ACTIVA o
        // CONFIRMADA, idempotente sobre ENTREGADA. Un unico SaveChanges.
        var consumido = await servicio.ConsumirAsync(ordenId, cancelacion);
        if (!consumido)
        {
            return Results.NotFound(new { ordenId, consumido = false });
        }

        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(new { ordenId, consumido = true });
    }
    catch (Exception ex) when (ex is ExcepcionConflictoConcurrencia or ExcepcionConsumoReservaInconsistente)
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
    catch (Exception ex) when (ex is ExcepcionCantidadFisicaMenorQueReservada or ExcepcionConflictoConcurrencia)
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

inventario.MapGet("/existencias/{sku}", async (
    string sku,
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarPorSkuAsync(sku, cancelacion)));

// HU-016 / HU-017: vista unificada de lectura sobre las 32 tiendas y los 2 centros
// de distribucion. Todo lo que sigue lee directo de Postgres, sin cache: el
// disponible que devuelve ya descuenta las reservas vigentes porque reservar y
// liberar mueven cantidad_reservada en la misma transaccion, asi que no hay
// ventana en la que estas consultas muestren stock que en realidad esta
// comprometido.

inventario.MapGet("/existencias", async (
    string? ubicacionId,
    TipoUbicacion? tipoUbicacion,
    int? pagina,
    int? tamano,
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarAsync(
        ubicacionId,
        tipoUbicacion,
        pagina ?? 1,
        tamano ?? 50,
        cancelacion)));

// Tienda activa mas cercana a "desde" que tiene el SKU disponible (Haversine sobre
// el catalogo de sucursales). La consulta el catalogo web cuando la sucursal
// elegida esta agotada.
inventario.MapGet("/existencias/{sku}/cercana", async (
    string sku,
    string? desde,
    int? cantidad,
    ServicioSucursales servicio,
    CancellationToken cancelacion) =>
{
    try
    {
        var cercana = await servicio.BuscarCercanaAsync(sku, desde, cantidad ?? 1, cancelacion);

        return cercana is null
            ? Results.Problem(
                title: "Sin existencias cercanas",
                detail: $"Ninguna tienda tiene {cantidad ?? 1} unidad(es) disponibles de {sku}.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["codigo"] = "sin_existencias", ["sku"] = sku })
            : Results.Ok(cercana);
    }
    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
});

inventario.MapGet("/existencias/{sku}/movimientos", async (
    string sku,
    string? ubicacionId,
    DateTime? desde,
    DateTime? hasta,
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarMovimientosAsync(sku, ubicacionId, desde, hasta, cancelacion)));

// Catalogo publico de sucursales activas (tiendas y centros de distribucion) con
// nombre visible y coordenadas. Lo consume la Web para armar sus selectores.
inventario.MapGet("/sucursales", async (
    ServicioSucursales servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarActivasAsync(cancelacion)));

inventario.MapGet("/ubicaciones", async (
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarUbicacionesAsync(cancelacion)));

inventario.MapGet("/ubicaciones/{ubicacionId}/existencias", async (
    string ubicacionId,
    ServicioExistencias servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarPorUbicacionAsync(ubicacionId, cancelacion)));

inventario.MapGet("/ubicaciones/{ubicacionId}/reservas", async (
    string ubicacionId,
    EstadoReserva? estado,
    ServicioReservas servicio,
    CancellationToken cancelacion) =>
    Results.Ok(await servicio.ListarPorUbicacionAsync(ubicacionId, estado, cancelacion)));

// Retenciones de stock mientras el producto esta en el carrito. Las llama Auth por
// la red interna (Inventario:BaseUrl). A proposito fuera de /api/inventario: el
// Gateway no tiene ninguna ruta para /interno/, asi que nadie de afuera puede
// retener ni liberar el stock de un carrito ajeno. Mismo manejo de concurrencia que
// la reserva de una orden: un unico SaveChanges y el choque de version es 409.
var carritos = app.MapGroup("/interno/inventario/carritos");

// Crea o ajusta la retencion de un SKU a la cantidad total indicada. 409 con
// {sku, disponible, mensaje, sucursalCercana} si no hay stock en ninguna parte.
carritos.MapPut("/{carritoId}/items/{sku}", (
    string carritoId,
    string sku,
    RetenerItemCarritoRequest solicitud,
    ServicioReservasCarrito servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
    OperarCarritoAsync(async () =>
    {
        var linea = await servicio.RetenerAsync(carritoId, sku, solicitud.Cantidad, solicitud.UbicacionPreferidaId, cancelacion);
        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(linea);
    }));

carritos.MapDelete("/{carritoId}/items/{sku}", (
    string carritoId,
    string sku,
    ServicioReservasCarrito servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
    OperarCarritoAsync(async () =>
    {
        if (await servicio.LiberarItemAsync(carritoId, sku, cancelacion))
        {
            await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        }

        return Results.NoContent();
    }));

carritos.MapDelete("/{carritoId}", (
    string carritoId,
    ServicioReservasCarrito servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
    OperarCarritoAsync(async () =>
    {
        if (await servicio.LiberarCarritoAsync(carritoId, cancelacion) > 0)
        {
            await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        }

        return Results.NoContent();
    }));

carritos.MapPost("/{carritoId}/renovar", (
    string carritoId,
    RenovarCarritoRequest? solicitud,
    ServicioReservasCarrito servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
    OperarCarritoAsync(async () =>
    {
        var carrito = await servicio.RenovarAsync(carritoId, solicitud?.Skus, cancelacion);
        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(carrito);
    }));

carritos.MapPost("/{carritoId}/transferir", (
    string carritoId,
    TransferirCarritoRequest solicitud,
    ServicioReservasCarrito servicio,
    IUnidadDeTrabajo unidadDeTrabajo,
    CancellationToken cancelacion) =>
    OperarCarritoAsync(async () =>
    {
        var carrito = await servicio.TransferirAsync(carritoId, solicitud.DestinoCarritoId, cancelacion);
        await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        return Results.Ok(carrito);
    }));

carritos.MapGet("/{carritoId}", (
    string carritoId,
    ServicioReservasCarrito servicio,
    CancellationToken cancelacion) =>
    OperarCarritoAsync(async () => Results.Ok(await servicio.ObtenerAsync(carritoId, cancelacion))));

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<InventarioDbContext>();
    contexto.Database.Migrate();
}

app.Run();

static async Task<IResult> OperarCarritoAsync(Func<Task<IResult>> operacion)
{
    try
    {
        return await operacion();
    }
    catch (ExcepcionSinStockCarrito ex)
    {
        return Results.Conflict(ex.ACuerpo());
    }
    catch (ExcepcionStockInsuficiente ex)
    {
        return Results.Conflict(new SinStockCarritoDto(ex.Sku, ex.Disponible, ex.Message, null));
    }
    catch (ExcepcionConflictoConcurrencia ex)
    {
        return MapearError(ex);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(
            title: "Solicitud invalida",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
}

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

    ExcepcionCantidadFisicaMenorQueReservada => Results.Problem(
        title: "Cantidad fisica menor que la reservada",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "fisica_menor_que_reservada" }),

    ExcepcionConsumoReservaInconsistente => Results.Problem(
        title: "Reserva inconsistente",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["codigo"] = "reserva_inconsistente" }),

    _ => Results.Problem(
        title: "Error interno",
        detail: ex.Message,
        statusCode: StatusCodes.Status500InternalServerError)
};
