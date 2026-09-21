using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.BuildingBlocks.Extensions;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

// ── ADR-020: Autenticacion en el borde ────────────────────────────────────────
// La bandera Gateway:ExigirJwt (variable de entorno Gateway__ExigirJwt /
// docker-compose GATEWAY_EXIGIR_JWT) activa la validacion del JWT emitido por Auth.
// Por defecto es false: el gateway enruta sin validar, compatible con scripts de
// carga y de camino feliz que no envian token.
var exigirJwt = builder.Configuration.GetValue<bool>("Gateway:ExigirJwt");

if (exigirJwt)
{
    var jwtSecret = builder.Configuration["Auth:JwtSecret"]
        ?? throw new InvalidOperationException(
            "Gateway:ExigirJwt = true pero Auth:JwtSecret no esta configurado. " +
            "Asegurate de que la variable de entorno JWT_SECRET este presente.");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opciones =>
        {
            // Mismos parametros que Auth.Api: firma y vencimiento.
            // Sin validacion de emisor ni de audiencia (MVP, red interna Docker).
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
}

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSgoDefaults();

if (exigirJwt)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// El middleware de autorizacion del gateway se aplica directamente sobre el pipeline
// del proxy. Las rutas protegidas/anonimas se declaran en appsettings.json con la
// politica "RequireAuthenticatedUser" en las rutas de ordenes, pagos, etc.
app.MapReverseProxy();

app.Run();
