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
using SgoLogic.Pagos.Api.Consumers;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

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

// Tarjetas guardadas: el cliente sale del JWT que emite Auth. El gateway lo reenvia
// tal cual, asi que Pagos lo valida con el mismo secreto (Auth:JwtSecret). Sin
// secreto configurado se usa una clave aleatoria: ningun token valida y los
// endpoints de tarjetas responden 401, sin tumbar el resto del servicio.
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

// Boveda de PayKlik (token de un solo uso desde una tarjeta guardada). Llamada
// directa, sin el disyuntor de cobros: es una operacion interactiva del checkout.
builder.Services.AddHttpClient<IClienteBovedaPayKlik, ClienteBovedaPayKlikHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<PayKlikOptions>>().Value;
    http.BaseAddress = new Uri(opciones.BaseUrl);
    http.Timeout = TimeSpan.FromMilliseconds(opciones.TimeoutMs);
});
builder.Services.AddScoped<IRepositorioTarjetasGuardadas, RepositorioTarjetasGuardadas>();
builder.Services.AddScoped<ServicioTarjetasGuardadas>();

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
