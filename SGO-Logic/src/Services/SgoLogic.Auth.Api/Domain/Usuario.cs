namespace SgoLogic.Auth.Api.Domain;

/// <summary>
/// Entidad de dominio Usuario. Solo almacena identidad y credenciales; el
/// carrito vive en Redis, no aqui. hashContrasena usa BCrypt (no se almacena
/// la contrasena en texto plano en ningun momento).
/// </summary>
public sealed class Usuario
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = default!;
    public string HashContrasena { get; private set; } = default!;
    public string NombreCompleto { get; private set; } = default!;
    public bool EmailVerificado { get; private set; }
    public string Rol { get; private set; } = "CLIENTE";
    public DateTime CreadoEn { get; private set; }
    public DateTime? ActualizadoEn { get; private set; }

    // Constructor EF Core
    private Usuario() { }

    public static Usuario Crear(string email, string hashContrasena, string nombreCompleto, string rol = "CLIENTE")
    {
        return new Usuario
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant().Trim(),
            HashContrasena = hashContrasena,
            NombreCompleto = nombreCompleto,
            EmailVerificado = false,
            Rol = string.IsNullOrWhiteSpace(rol) ? "CLIENTE" : rol.Trim().ToUpperInvariant(),
            CreadoEn = DateTime.UtcNow
        };
    }

    public void MarcarEmailVerificado()
    {
        EmailVerificado = true;
        ActualizadoEn = DateTime.UtcNow;
    }

    public void AsignarRol(string nuevoRol)
    {
        Rol = string.IsNullOrWhiteSpace(nuevoRol) ? "CLIENTE" : nuevoRol.Trim().ToUpperInvariant();
        ActualizadoEn = DateTime.UtcNow;
    }

    public void ActualizarHash(string nuevoHash)
    {
        HashContrasena = nuevoHash;
        ActualizadoEn = DateTime.UtcNow;
    }

    public void ActualizarNombre(string nuevoNombre)
    {
        if (!string.IsNullOrWhiteSpace(nuevoNombre))
        {
            NombreCompleto = nuevoNombre.Trim();
            ActualizadoEn = DateTime.UtcNow;
        }
    }
}
