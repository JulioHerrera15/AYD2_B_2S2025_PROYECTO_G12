using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.Auth.Api.Application;
using SgoLogic.Auth.Api.Domain;
using Xunit;

namespace SgoLogic.Auth.Tests.Application;

public sealed class JwtServiceTests
{
    private const string Secret = "clave_secreta_super_segura_de_al_menos_32_caracteres_12345";

    private static JwtService CrearSut(int expiracionMinutos = 60)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Auth:JwtSecret"] = Secret,
            ["Auth:JwtExpiracionMinutos"] = expiracionMinutos.ToString()
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
        return new JwtService(config);
    }

    [Fact]
    public void GenerarToken_produce_jwt_valido_con_claims_correctos()
    {
        var sut = CrearSut(expiracionMinutos: 45);
        var usuario = Usuario.Crear("jwt@megastore.com", "dummy_hash", "Carlos Mendez");

        var response = sut.GenerarToken(usuario);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Token);
        Assert.Equal("jwt@megastore.com", response.Email);
        Assert.Equal("Carlos Mendez", response.NombreCompleto);
        Assert.Equal(usuario.Id.ToString(), response.UsuarioId);

        // Validar firma del token usando TokenValidationParameters
        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };

        var principal = handler.ValidateToken(response.Token, validationParams, out var validatedToken);

        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);

        var jwtToken = validatedToken as JwtSecurityToken;
        Assert.NotNull(jwtToken);
        Assert.Equal(usuario.Id.ToString(), jwtToken.Subject);
        Assert.Equal("jwt@megastore.com", jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal("Carlos Mendez", jwtToken.Claims.FirstOrDefault(c => c.Type == "nombre")?.Value);
    }

    [Fact]
    public void GenerarToken_sin_secret_lanza_InvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() => new JwtService(config));
    }
}
