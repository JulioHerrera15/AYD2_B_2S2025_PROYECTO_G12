namespace SgoLogic.Auth.Api.Domain;

/// <summary>
/// Codigo de verificacion de 6 digitos enviado al correo del usuario.
/// El bloqueo de login hasta verificar el correo se implementa revisando
/// Usuario.EmailVerificado antes de emitir el JWT.
/// </summary>
public sealed class CodigoVerificacion
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }

    /// <summary>Codigo de 6 digitos almacenado hasheado con BCrypt.</summary>
    public string HashCodigo { get; private set; } = default!;

    public DateTime ExpiraEn { get; private set; }
    public bool Usado { get; private set; }
    public DateTime CreadoEn { get; private set; }

    // Navegacion EF Core
    public Usuario? Usuario { get; private set; }

    private CodigoVerificacion() { }

    public static CodigoVerificacion Crear(Guid usuarioId, string hashCodigo, int ttlMinutos = 15)
    {
        return new CodigoVerificacion
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            HashCodigo = hashCodigo,
            ExpiraEn = DateTime.UtcNow.AddMinutes(ttlMinutos),
            Usado = false,
            CreadoEn = DateTime.UtcNow
        };
    }

    public bool EstaVigente() => !Usado && DateTime.UtcNow < ExpiraEn;

    public void MarcarUsado()
    {
        Usado = true;
    }
}
