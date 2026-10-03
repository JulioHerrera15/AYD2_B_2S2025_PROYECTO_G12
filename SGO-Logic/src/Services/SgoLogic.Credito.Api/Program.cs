using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Consumers;
using SgoLogic.Credito.Api.Controllers;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Api.Infrastructure.Demo;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;
using SgoLogic.Credito.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

var opcionesCredito = builder.Configuration.GetSection(CreditoOptions.Seccion).Get<CreditoOptions>() ?? new CreditoOptions();

// /interno/credito/historiales solo existe con Credito:EndpointsInternos=true.
builder.Services.AddControllers().ConfigureApplicationPartManager(partes =>
{
    if (!opcionesCredito.EndpointsInternos)
    {
        partes.FeatureProviders.Add(new SinControladoresInternos());
    }
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<CreditoDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<CreditoDbContext>(builder.Configuration);

// El cliente sale del JWT que emite Auth, reenviado tal cual por el gateway. Misma
// configuracion que Pagos: sin secreto se usa una clave aleatoria y ningun token
// valida (401), sin tumbar el servicio.
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

builder.Services.Configure<CreditoOptions>(builder.Configuration.GetSection(CreditoOptions.Seccion));
builder.Services.Configure<PoliticaCreditoOptions>(builder.Configuration.GetSection(PoliticaCreditoOptions.Seccion));
builder.Services.AddSingleton(proveedor => new PoliticaCredito(proveedor.GetRequiredService<IOptions<PoliticaCreditoOptions>>()));

builder.Services.AddScoped<IRepositorioCredito, RepositorioCredito>();
builder.Services.AddScoped<IConsultasCredito, ConsultasCredito>();
builder.Services.AddScoped<ServicioPerfilCrediticio>();
builder.Services.AddScoped<ServicioElegibilidad>();
builder.Services.AddScoped<ServicioEvaluacionCredito>();
builder.Services.AddScoped<ServicioAnulacionCargos>();
builder.Services.AddScoped<ServicioDemoCredito>();

// Bloque 3: cobranza (abonos, resultado del cobro, recordatorios y vencimientos).
builder.Services.Configure<CobranzaOptions>(builder.Configuration.GetSection(CobranzaOptions.Seccion));
builder.Services.AddScoped<ServicioAbonos>();
builder.Services.AddScoped<ServicioResultadoAbonos>();
builder.Services.AddScoped<ServicioVencimientos>();
builder.Services.AddHostedService<PagosCreditoConsumidor>();
builder.Services.AddHostedService<VencimientosCreditoWorker>();
builder.Services.AddHostedService<HistorialClienteErpConsumidor>();

// Bloque 2: evaluacion de compras a credito dentro de la saga y anulacion de cargos.
builder.Services.AddHostedService<OrdenesCreditoConsumidor>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<CreditoDbContext>();
    contexto.Database.Migrate();
}

if (opcionesCredito.SembrarDemo)
{
    await SembradorDemoCredito.SembrarAsync(
        app.Services,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SembradorDemoCredito)),
        CancellationToken.None);
}

app.Run();
