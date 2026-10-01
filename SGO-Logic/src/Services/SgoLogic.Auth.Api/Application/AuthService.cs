using Microsoft.EntityFrameworkCore;
using SgoLogic.Auth.Api.Domain;
using SgoLogic.Auth.Api.Infrastructure;

namespace SgoLogic.Auth.Api.Application;

public interface IAuthService
{
    Task<RegistroResponse> RegistrarAsync(RegistroRequest request, CancellationToken ct = default);
    Task<RegistroResponse> ReenviarCodigoAsync(string email, CancellationToken ct = default);
    Task<VerificarEmailResponse> VerificarEmailAsync(VerificarEmailRequest request, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AsignarRolResponse> AsignarRolAsync(AsignarRolRequest request, CancellationToken ct = default);
    Task<UsuarioDto> ObtenerUsuarioPorEmailAsync(string email, CancellationToken ct = default);
    Task<List<UsuarioDto>> ListarUsuariosAsync(CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private readonly AuthDbContext _contexto;
    private readonly IJwtService _jwt;
    private readonly ILogger<AuthService> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public AuthService(
        AuthDbContext contexto,
        IJwtService jwt,
        ILogger<AuthService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _contexto = contexto;
        _jwt = jwt;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

        // Hashing seguro con BCrypt (work factor 12 -- balance rendimiento/seguridad)
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Contrasena, workFactor: 12);
        var usuario = Usuario.Crear(emailNormalizado, hash, request.NombreCompleto);

        // Generar codigo de 6 digitos y guardarlo hasheado
        var codigoPlano = GenerarCodigo6Digitos();
        var hashCodigo = BCrypt.Net.BCrypt.HashPassword(codigoPlano, workFactor: 10);
        var codigo = CodigoVerificacion.Crear(usuario.Id, hashCodigo);

        _contexto.Usuarios.Add(usuario);
        _contexto.CodigosVerificacion.Add(codigo);
        await _contexto.SaveChangesAsync(ct);

        await EnviarCorreoVerificacionAsync(emailNormalizado, request.NombreCompleto, codigoPlano, ct);

        _logger.LogInformation(
            "DEMO -- Codigo de verificacion para {Email}: {Codigo}",
            emailNormalizado, codigoPlano);

        return new RegistroResponse(
            UsuarioId: usuario.Id.ToString(),
            Email: usuario.Email,
            Mensaje: "Registro exitoso. Revisa tu correo para obtener el codigo de verificacion.");
    }

    public async Task<RegistroResponse> ReenviarCodigoAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@") || !email.Contains("."))
            throw new ArgumentException("El formato del correo electronico es invalido.");

        var emailNormalizado = email.ToLowerInvariant().Trim();

        var usuario = await _contexto.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct)
            ?? throw new KeyNotFoundException("No se encontro un usuario registrado con ese correo.");

        if (usuario.EmailVerificado)
            return new RegistroResponse(usuario.Id.ToString(), usuario.Email, "El correo ya se encuentra verificado.");

        var codigoPlano = GenerarCodigo6Digitos();
        var hashCodigo = BCrypt.Net.BCrypt.HashPassword(codigoPlano, workFactor: 10);
        var codigo = CodigoVerificacion.Crear(usuario.Id, hashCodigo);

        _contexto.CodigosVerificacion.Add(codigo);
        await _contexto.SaveChangesAsync(ct);

        await EnviarCorreoVerificacionAsync(emailNormalizado, usuario.NombreCompleto, codigoPlano, ct);

        _logger.LogInformation(
            "DEMO -- Codigo de verificacion reenviado para {Email}: {Codigo}",
            emailNormalizado, codigoPlano);

        return new RegistroResponse(
            UsuarioId: usuario.Id.ToString(),
            Email: usuario.Email,
            Mensaje: "Se ha enviado un nuevo codigo de verificacion a tu correo.");
    }

    private async Task EnviarCorreoVerificacionAsync(string email, string? nombre, string codigoPlano, CancellationToken ct)
    {
        if (_httpClientFactory != null)
        {
            try
            {
                var cliente = _httpClientFactory.CreateClient("Mensajeria");
                var nombreDest = !string.IsNullOrWhiteSpace(nombre) ? nombre : email;
                var payload = new
                {
                    Destinatario = email,
                    Asunto = "Codigo de verificacion - MegaStore",
                    Cuerpo = $"Hola {nombreDest}, tu codigo de verificacion para activar tu cuenta en MegaStore es: {codigoPlano}. Valido por 15 minutos."
                };
                await cliente.PostAsJsonAsync("/api/mensajes/email", payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falla al enviar codigo de verificacion via mensajeria para {Email} - aislado", email);
            }
        }
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

    public async Task<AsignarRolResponse> AsignarRolAsync(AsignarRolRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("El correo electronico es requerido.");

        var rolNormalizado = string.IsNullOrWhiteSpace(request.Rol) ? "CLIENTE" : request.Rol.Trim().ToUpperInvariant();
        if (rolNormalizado != "CLIENTE" && rolNormalizado != "OPERADOR" && rolNormalizado != "ADMIN")
        {
            throw new ArgumentException($"El rol '{request.Rol}' no es valido. Roles oficiales del sistema: CLIENTE, OPERADOR, ADMIN.");
        }

        var emailNormalizado = request.Email.ToLowerInvariant().Trim();
        var usuario = await _contexto.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct)
            ?? throw new KeyNotFoundException($"No se encontro un usuario registrado con el correo {request.Email}.");

        usuario.AsignarRol(rolNormalizado);
        await _contexto.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rol actualizado en base de datos para usuario {Email}: {NuevoRol}",
            emailNormalizado, rolNormalizado);

        return new AsignarRolResponse(
            Email: usuario.Email,
            Rol: usuario.Rol,
            Mensaje: $"Rol asignado exitosamente: {usuario.Rol}");
    }

    public async Task<UsuarioDto> ObtenerUsuarioPorEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("El correo electronico es requerido.");

        var emailNormalizado = email.ToLowerInvariant().Trim();
        var usuario = await _contexto.Usuarios.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct)
            ?? throw new KeyNotFoundException($"No se encontro un usuario con el correo {email}.");

        return new UsuarioDto(
            Id: usuario.Id,
            Email: usuario.Email,
            NombreCompleto: usuario.NombreCompleto,
            Rol: usuario.Rol,
            EmailVerificado: usuario.EmailVerificado,
            CreadoEn: usuario.CreadoEn);
    }

    public async Task<List<UsuarioDto>> ListarUsuariosAsync(CancellationToken ct = default)
    {
        var usuarios = await _contexto.Usuarios.AsNoTracking()
            .OrderByDescending(u => u.CreadoEn)
            .ToListAsync(ct);

        return usuarios.Select(u => new UsuarioDto(
            Id: u.Id,
            Email: u.Email,
            NombreCompleto: u.NombreCompleto,
            Rol: u.Rol,
            EmailVerificado: u.EmailVerificado,
            CreadoEn: u.CreadoEn
        )).ToList();
    }

    private static string GenerarCodigo6Digitos()
    {
        // Usa Random criptograficamente seguro
        return Random.Shared.Next(100_000, 999_999).ToString();
    }
}

