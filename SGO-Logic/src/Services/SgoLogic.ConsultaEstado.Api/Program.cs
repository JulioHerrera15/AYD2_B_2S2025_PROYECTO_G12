using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.ConsultaEstado.Api.Application;
using SgoLogic.ConsultaEstado.Api.Consumers;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Base de datos propia de ConsultaEstado ────────────────────────────────────
builder.Services.AddDbContext<ConsultaEstadoDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ── Redis (modelo caliente) ───────────────────────────────────────────────────
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis no esta configurado");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnection));

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
builder.Services.AddSgoMessaging();

// ── Servicios de aplicacion ───────────────────────────────────────────────────
builder.Services.AddScoped<IConsultaEstadoService, ConsultaEstadoService>();

// ── Consumidor de eventos ─────────────────────────────────────────────────────
builder.Services.AddHostedService<ConsultaEstadoOrdenesConsumidor>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSgoDefaults();

app.MapControllers();

// Aplicar migraciones y sembrar ordenes de demostracion
using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<ConsultaEstadoDbContext>();
    contexto.Database.Migrate();

    if (!contexto.OrdenesVista.Any())
    {
        var ordenesIniciales = new List<OrdenVista>
        {
            new()
            {
                OrdenId = "ORD-2026-001",
                NumeroOrden = "000100",
                ClienteId = "CLI-001",
                Canal = "WEB",
                Estado = "CONFIRMADA",
                MetodoEntrega = "DOMICILIO",
                Moneda = "GTQ",
                Subtotal = 2500m,
                Impuestos = 300m,
                Total = 2800m,
                PagoId = "PAG-8831",
                DocumentoFiscalId = "FEL-SAT-GT-9921",
                SerieDocumentoFiscal = "A",
                NumeroDocumentoFiscal = "1001",
                UltimoSequence = 3,
                CreadaEn = DateTime.UtcNow.AddHours(-3),
                ActualizadaEn = DateTime.UtcNow.AddMinutes(-30)
            },
            new()
            {
                OrdenId = "ORD-8950",
                NumeroOrden = "008950",
                ClienteId = "CLI-002",
                Canal = "TIENDA",
                Estado = "CREADA",
                MetodoEntrega = "SUCURSAL",
                Moneda = "GTQ",
                Subtotal = 12678.57m,
                Impuestos = 1521.43m,
                Total = 14200.00m,
                UltimoSequence = 1,
                CreadaEn = DateTime.UtcNow.AddHours(-1),
                ActualizadaEn = DateTime.UtcNow.AddHours(-1)
            },
            new()
            {
                OrdenId = "ORD-8951",
                NumeroOrden = "008951",
                ClienteId = "CLI-003",
                Canal = "CALL_CENTER",
                Estado = "CANCELADA",
                MetodoEntrega = "DOMICILIO",
                Moneda = "GTQ",
                Subtotal = 758.93m,
                Impuestos = 91.07m,
                Total = 850.00m,
                PagoId = "PAG-4122",
                MotivoUltimoCambio = "Solicitud del cliente por error de dirección",
                UltimoSequence = 4,
                CreadaEn = DateTime.UtcNow.AddDays(-1),
                ActualizadaEn = DateTime.UtcNow.AddHours(-5)
            },
            new()
            {
                OrdenId = "ORD-8952",
                NumeroOrden = "008952",
                ClienteId = "CLI-004",
                Canal = "MOVIL",
                Estado = "ENTREGADA",
                MetodoEntrega = "DOMICILIO",
                Moneda = "GTQ",
                Subtotal = 3392.86m,
                Impuestos = 407.14m,
                Total = 3800.00m,
                PagoId = "PAG-9931",
                DocumentoFiscalId = "FEL-SAT-GT-4011",
                UltimoSequence = 5,
                CreadaEn = DateTime.UtcNow.AddDays(-2),
                ActualizadaEn = DateTime.UtcNow.AddHours(-2)
            }
        };

        contexto.OrdenesVista.AddRange(ordenesIniciales);
        contexto.SaveChanges();

        try
        {
            var redisMultiplexer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var redisDb = redisMultiplexer.GetDatabase();
            foreach (var ord in ordenesIniciales)
            {
                redisDb.StringSet($"orden:{ord.OrdenId}", System.Text.Json.JsonSerializer.Serialize(ord), TimeSpan.FromHours(24));
            }
        }
        catch { /* Redis no bloqueante para migraciones */ }
    }
}

app.Run();
