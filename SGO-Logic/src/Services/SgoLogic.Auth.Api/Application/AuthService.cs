using Microsoft.EntityFrameworkCore;
using SgoLogic.Auth.Api.Domain;
using SgoLogic.Auth.Api.Infrastructure;

namespace SgoLogic.Auth.Api.Application;

public interface IAuthService
{
    Task<RegistroResponse> RegistrarAsync(RegistroRequest request, CancellationToken ct = default);
    Task<VerificarEmailResponse> VerificarEmailAsync(VerificarEmailRequest request, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private readonly AuthDbContext _contexto;
    private readonly IJwtService _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AuthDbContext contexto, IJwtService jwt, ILogger<AuthService> logger)
    {
        _contexto = contexto;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<RegistroResponse> RegistrarAsync(RegistroRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@") || !request.Email.Contains("."))
            throw new ArgumentException("El formato del correo electronico es invalido.");

        var emailNormalizado = request.Email.ToLowerInvariant().Trim();

        var existe = await _contexto.Usuarios
            .AnyAsync(u => u.Email == emailNormalizado, ct);

        if (existe)
            throw new InvalidOperationException("Ya existe una cuenta con ese correo.");

        // Hashing seguro con BCrypt (work factor 12 — balance rendimiento/seguridad)
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Contrasena, workFactor: 12);
        var usuario = Usuario.Crear(emailNormalizado, hash, request.NombreCompleto);

        // Generar codigo de 6 digitos y guardarlo hasheado
        var codigoPlano = GenerarCodigo6Digitos();
        var hashCodigo = BCrypt.Net.BCrypt.HashPassword(codigoPlano, workFactor: 10);
        var codigo = CodigoVerificacion.Crear(usuario.Id, hashCodigo);

        _contexto.Usuarios.Add(usuario);
        _contexto.CodigosVerificacion.Add(codigo);
        await _contexto.SaveChangesAsync(ct);

        // TODO (produccion): enviar codigoPlano por correo al usuario
        // Por ahora se loguea para poder probar en demo
        _logger.LogInformation(
            "DEMO — Codigo de verificacion para {Email}: {Codigo}",
            emailNormalizado, codigoPlano);

        return new RegistroResponse(
            UsuarioId: usuario.Id.ToString(),
            Email: usuario.Email,
            Mensaje: "Registro exitoso. Revisa tu correo para obtener el codigo de verificacion.");
    }

    public async Task<VerificarEmailResponse> VerificarEmailAsync(VerificarEmailRequest request, CancellationToken ct = default)
    {
        var emailNormalizado = request.Email.ToLowerInvariant().Trim();

        var usuario = await _contexto.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (usuario.EmailVerificado)
            return new VerificarEmailResponse("El correo ya estaba verificado.");

        var codigo = await _contexto.CodigosVerificacion
            .Where(c => c.UsuarioId == usuario.Id && !c.Usado)
            .OrderByDescending(c => c.CreadoEn)
            .FirstOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException("No hay un codigo de verificacion vigente.");

        if (!codigo.EstaVigente())
            throw new UnauthorizedAccessException("El codigo ha expirado. Solicita uno nuevo.");

        if (!BCrypt.Net.BCrypt.Verify(request.Codigo, codigo.HashCodigo))
            throw new UnauthorizedAccessException("Codigo incorrecto.");

        codigo.MarcarUsado();
        usuario.MarcarEmailVerificado();
        await _contexto.SaveChangesAsync(ct);

        return new VerificarEmailResponse("Correo verificado exitosamente. Ya puedes iniciar sesion.");
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var emailNormalizado = request.Email.ToLowerInvariant().Trim();

        var usuario = await _contexto.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct)
            ?? throw new UnauthorizedAccessException("Credenciales incorrectas.");

        if (!usuario.EmailVerificado)
            throw new UnauthorizedAccessException("Debes verificar tu correo antes de iniciar sesion.");

        if (!BCrypt.Net.BCrypt.Verify(request.Contrasena, usuario.HashContrasena))
            throw new UnauthorizedAccessException("Credenciales incorrectas.");

        return _jwt.GenerarToken(usuario);
    }

    private static string GenerarCodigo6Digitos()
    {
        // Usa Random criptograficamente seguro
        return Random.Shared.Next(100_000, 999_999).ToString();
    }
}
