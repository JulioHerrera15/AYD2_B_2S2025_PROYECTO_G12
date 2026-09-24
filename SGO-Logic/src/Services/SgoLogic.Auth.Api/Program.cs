using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.Auth.Api.Application;
using SgoLogic.Auth.Api.Infrastructure;
using SgoLogic.BuildingBlocks.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Base de datos propia de Auth ─────────────────────────────────────────────
builder.Services.AddDbContext<AuthDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ── Redis (carrito unificado) ─────────────────────────────────────────────────
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis no esta configurado");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnection));

// ── JWT ───────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Auth:JwtSecret"]
    ?? throw new InvalidOperationException("Auth:JwtSecret no esta configurado");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ── Servicios de aplicacion ───────────────────────────────────────────────────
var mockMensajeriaUrl = builder.Configuration["Mensajeria:MockBaseUrl"] ?? "http://mock-mensajeria:8080";
builder.Services.AddHttpClient("Mensajeria", http =>
{
    http.BaseAddress = new Uri(mockMensajeriaUrl);
    http.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ICarritoService, CarritoService>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSgoDefaults();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Aplicar migraciones al arrancar (patron de MVP con Docker Compose)
using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    contexto.Database.Migrate();
}

app.Run();
