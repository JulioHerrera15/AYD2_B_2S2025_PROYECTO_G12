using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Consumers;
using SgoLogic.Notificaciones.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Base de datos (solo para tabla mensajes_procesados) ───────────────────────
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

// ── Consumidor de eventos ─────────────────────────────────────────────────────
builder.Services.AddHostedService<NotificacionesOrdenesConsumidor>();

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
