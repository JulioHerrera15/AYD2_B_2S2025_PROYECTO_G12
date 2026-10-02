using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Consumers;
using SgoLogic.Notificaciones.Api.Infrastructure;
using SgoLogic.Notificaciones.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Base de datos (mensajes_procesados y notificacion_pendiente) ──────────────
builder.Services.AddDbContext<NotificacionesDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
builder.Services.AddSgoMessaging();

// ── Cliente HTTP hacia MockMensajeria ─────────────────────────────────────────
var mockUrl = builder.Configuration["Mensajeria:MockBaseUrl"]
    ?? throw new InvalidOperationException("Mensajeria:MockBaseUrl no esta configurado");
builder.Services.AddHttpClient<IMockMensajeriaClient, MockMensajeriaClient>(http =>
{
    http.BaseAddress = new Uri(mockUrl);
    http.Timeout = TimeSpan.FromSeconds(10);
});

// ── Envio con reintentos (B3) ─────────────────────────────────────────────────
// Un envio fallido queda en notificacion_pendiente y el worker lo reintenta con
// backoff exponencial (10 s, 20 s, ... hasta 6 reintentos; luego FALLIDA).
builder.Services.AddScoped<IRepositorioNotificacionesPendientes, RepositorioNotificacionesPendientes>();
builder.Services.AddScoped<ServicioNotificaciones>();
builder.Services.AddHostedService<ReintentoNotificacionesWorker>();

// ── Consumidores de eventos ───────────────────────────────────────────────────
builder.Services.AddHostedService<NotificacionesOrdenesConsumidor>();
// Consumidor separado para eventos de Despacho (HU-020 — listo-para-recoger)
// Cola independiente para respetar la topologia cerrada de notificaciones.ordenes
builder.Services.AddHostedService<NotificacionesDespachoConsumidor>();
// Consumidor separado para eventos de Pagos pendientes (enlace de reintento de cobro)
builder.Services.AddHostedService<NotificacionesPagosConsumidor>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSgoDefaults();

app.MapControllers();

// Aplicar migraciones al arrancar
using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<NotificacionesDbContext>();
    contexto.Database.Migrate();
}

app.Run();
