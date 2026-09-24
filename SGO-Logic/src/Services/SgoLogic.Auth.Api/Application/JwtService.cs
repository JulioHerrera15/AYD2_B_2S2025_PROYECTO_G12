using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SgoLogic.Auth.Api.Domain;

namespace SgoLogic.Auth.Api.Application;

public interface IJwtService
{
    LoginResponse GenerarToken(Usuario usuario);
}

public sealed class JwtService : IJwtService
{
    private readonly string _secret;
    private readonly int _expiracionMinutos;

    public JwtService(IConfiguration config)
    {
        _secret = config["Auth:JwtSecret"]
            ?? throw new InvalidOperationException("Auth:JwtSecret no esta configurado");
        _expiracionMinutos = config.GetValue<int>("Auth:JwtExpiracionMinutos", 60);
    }

    public LoginResponse GenerarToken(Usuario usuario)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expira = DateTime.UtcNow.AddMinutes(_expiracionMinutos);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim("nombre", usuario.NombreCompleto),
            new Claim("rol", usuario.Rol),
            new Claim(ClaimTypes.Role, usuario.Rol),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expira,
            signingCredentials: creds);

        return new LoginResponse(
            Token: new JwtSecurityTokenHandler().WriteToken(token),
            UsuarioId: usuario.Id.ToString(),
            Email: usuario.Email,
            NombreCompleto: usuario.NombreCompleto,
            ExpiraEn: expira,
            Rol: usuario.Rol);
    }
}
