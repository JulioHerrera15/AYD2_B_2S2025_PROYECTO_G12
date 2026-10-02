using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Consumers;
using SgoLogic.Pagos.Api.Infrastructure.Boveda;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<PagosDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<PagosDbContext>(builder.Configuration);

builder.Services.Configure<PayKlikOptions>(builder.Configuration.GetSection(PayKlikOptions.Seccion));
builder.Services.Configure<ReintentoOptions>(builder.Configuration.GetSection(ReintentoOptions.Seccion));

// El HttpClient tipado invoca a PayKlik "a pelo"; el circuit breaker (ADR-009) lo
// envuelve con ClientePayKlikConDisyuntor, que es a quien resuelve IClientePayKlik.
builder.Services.AddHttpClient<ClientePayKlikHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<PayKlikOptions>>().Value;
    http.BaseAddress = new Uri(opciones.BaseUrl);
    http.Timeout = TimeSpan.FromMilliseconds(opciones.TimeoutMs);
});

builder.Services.AddSingleton<DisyuntorPayKlik>();
builder.Services.AddScoped<IClientePayKlik>(proveedor => new ClientePayKlikConDisyuntor(
    proveedor.GetRequiredService<ClientePayKlikHttp>(),
    proveedor.GetRequiredService<DisyuntorPayKlik>()));

// Tarjetas guardadas y tokenizacion: el cliente sale del JWT que emite Auth. El
// gateway lo reenvia tal cual, asi que Pagos lo valida con el mismo secreto
// (Auth:JwtSecret). Sin secreto configurado se usa una clave aleatoria: ningun token
// valida, los endpoints de tarjetas responden 401 y la tokenizacion trata a todos
// como invitados, sin tumbar el resto del servicio.
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

builder.Services.AddScoped<IRepositorioTarjetasGuardadas, RepositorioTarjetasGuardadas>();
builder.Services.AddScoped<ServicioTarjetasGuardadas>();

// Boveda transitoria de tokens (A2). El cifrador se construye aqui mismo: si
// PAGOS_CLAVE_CIFRADO falta o no son 32 bytes en base64, el servicio no arranca.
builder.Services.Configure<BovedaOptions>(builder.Configuration.GetSection(BovedaOptions.Seccion));
var cifrador = new CifradorAesGcm(builder.Configuration[$"{BovedaOptions.Seccion}:ClaveCifrado"]);
builder.Services.AddSingleton<ICifradorDatosTarjeta>(cifrador);
builder.Services.AddScoped<IRepositorioTokensPago, RepositorioTokensPago>();
builder.Services.AddScoped<ServicioTokensPago>();
builder.Services.AddHostedService<ExpiracionTokensWorker>();

builder.Services.AddScoped<ServicioCobro>();
builder.Services.AddScoped<ServicioReembolso>();
builder.Services.AddHostedService<PagosEventosConsumidor>();

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
    var contexto = scope.ServiceProvider.GetRequiredService<PagosDbContext>();
    contexto.Database.Migrate();
}

app.Run();
