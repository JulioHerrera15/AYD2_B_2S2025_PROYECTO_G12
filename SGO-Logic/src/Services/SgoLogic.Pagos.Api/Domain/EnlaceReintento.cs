using System.Security.Cryptography;

namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Enlace para reintentar el cobro de una orden cuyo pago quedo PENDIENTE
/// (EV-28, ADR-010). El token es opaco: 32 hex de bytes aleatorios, no expone
/// nada del medio de pago. Un enlace se usa una sola vez y expira.
/// </summary>
public sealed class EnlaceReintento
{
    private EnlaceReintento()
    {
        Token = string.Empty;
        OrdenId = string.Empty;
    }

    public EnlaceReintento(string ordenId, DateTime expiraEn, DateTime? creadoEn = null)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(ordenId));
        }

        Id = Guid.NewGuid();
        Token = GenerarToken();
        OrdenId = ordenId;
        ExpiraEn = expiraEn.ToUniversalTime();
        Usado = false;
        CreadoEn = (creadoEn ?? DateTime.UtcNow).ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public string Token { get; private set; }

    public string OrdenId { get; private set; }

    public DateTime ExpiraEn { get; private set; }

    public bool Usado { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public bool EstaVigente(DateTime ahora) => !Usado && ExpiraEn > ahora.ToUniversalTime();

    public void MarcarUsado() => Usado = true;

    private static string GenerarToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
