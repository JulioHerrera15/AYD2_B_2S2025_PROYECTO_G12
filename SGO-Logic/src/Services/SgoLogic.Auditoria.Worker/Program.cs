using Microsoft.EntityFrameworkCore;
using SgoLogic.Auditoria.Worker.Application;
using SgoLogic.Auditoria.Worker.Consumers;
using SgoLogic.Auditoria.Worker.Infrastructure;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ConnectionStrings:Postgres es el usuario restringido (PG_AUDIT_USER: solo
// INSERT+SELECT sobre eventos_auditoria). Es el que usan, en tiempo de
// ejecucion, tanto el consumidor como la API de consulta.
builder.Services.AddDbContext<AuditoriaDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();

builder.Services.AddSingleton<RegistradorEventoAuditoria>();
builder.Services.AddScoped<IAuditoriaConsultaService, AuditoriaConsultaService>();
builder.Services.AddHostedService<AuditoriaConsumidor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSgoDefaults();

app.MapControllers();

// ── Migracion y permisos: SIEMPRE con el usuario propietario ─────────────────
// ConnectionStrings:PostgresMigracion es el usuario propietario de la base
// (POSTGRES_USER/POSTGRES_PASSWORD): el unico con permisos para crear tablas,
// crear el rol restringido y otorgarle privilegios. El usuario de la app
// (ConnectionStrings:Postgres) nunca podria correr esta migracion: no tiene
// permiso para CREATE TABLE. Ver documentacion/docs/AUDITORIA.md.
var cadenaMigracion = builder.Configuration.GetConnectionString("PostgresMigracion")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgresMigracion no esta configurado");

var opcionesMigracion = new DbContextOptionsBuilder<AuditoriaDbContext>()
    .UseNpgsql(cadenaMigracion)
    .Options;

using (var contextoMigracion = new AuditoriaDbContext(opcionesMigracion))
{
    contextoMigracion.Database.Migrate();
}

var usuarioApp = builder.Configuration["Auditoria:AppUsuario"]
    ?? throw new InvalidOperationException("Auditoria:AppUsuario no esta configurado");
var passwordApp = builder.Configuration["Auditoria:AppPassword"]
    ?? throw new InvalidOperationException("Auditoria:AppPassword no esta configurado");

await PermisosAuditoriaAplicador.AplicarAsync(cadenaMigracion, usuarioApp, passwordApp, CancellationToken.None);

app.Run();