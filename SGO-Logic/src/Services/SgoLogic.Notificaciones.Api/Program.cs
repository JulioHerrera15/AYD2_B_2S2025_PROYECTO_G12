using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Consumers;
using SgoLogic.Notificaciones.Api.Infrastructure;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;
using SgoLogic.Notificaciones.Api.Kernel.Core;
using SgoLogic.Notificaciones.Api.Kernel.Plugins;
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

// ── Repositorio de Notificaciones Pendientes ──────────────────────────────────
builder.Services.AddScoped<IRepositorioNotificacionesPendientes, RepositorioNotificacionesPendientes>();

// ── Estilo Arquitectónico: Microkernel (Plugin-based Architecture) ────────────
builder.Services.AddSingleton<IPluginRegistry>(sp =>
{
    var registry = new PluginRegistry();

    // Inyección de plugins desacoplados
    var emailPlugin = ActivatorUtilities.CreateInstance<EmailNotificacionPlugin>(sp);
    var smsPlugin = ActivatorUtilities.CreateInstance<SmsNotificacionPlugin>(sp);
    var pushPlugin = ActivatorUtilities.CreateInstance<PushNotificacionPlugin>(sp);
    var webhookPlugin = ActivatorUtilities.CreateInstance<WebhookNotificacionPlugin>(sp);

    registry.Registrar(emailPlugin);
    registry.Registrar(smsPlugin);
    registry.Registrar(pushPlugin);
    registry.Registrar(webhookPlugin);

    return registry;
});

builder.Services.AddScoped<INotificacionesKernel, NotificacionesKernel>();

// ── Fachada de Servicio de Notificaciones y Worker de Reintentos ──────────────
builder.Services.AddScoped<ServicioNotificaciones>();
builder.Services.AddHostedService<ReintentoNotificacionesWorker>();

// ── Consumidores de eventos ───────────────────────────────────────────────────
builder.Services.AddHostedService<NotificacionesOrdenesConsumidor>();
// Consumidor separado para eventos de Despacho (HU-020 — listo-para-recoger)
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
