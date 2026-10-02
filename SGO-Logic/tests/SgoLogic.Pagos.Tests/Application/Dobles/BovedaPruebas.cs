using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Boveda;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>
/// Utilidades de prueba de la boveda transitoria: una clave fija de 32 bytes, el
/// cifrador real (AES-GCM no necesita doble) y fabricas de <see cref="TokenPago" />.
/// </summary>
public static class BovedaPruebas
{
    /// <summary>Bytes 0..31 en base64: solo para pruebas.</summary>
    public const string ClaveBase64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public const string PanVisa = "4242424242424242";
    public const string Cvv = "731";
    public const string Titular = "Ana Lopez";

    public static CifradorAesGcm Cifrador { get; } = new(ClaveBase64);

    /// <summary>Token de una tarjeta nueva, cifrado con la clave de prueba.</summary>
    public static TokenPago TokenNuevo(
        string id,
        string numero = PanVisa,
        string cvv = Cvv,
        int mesExp = 12,
        int? anioExp = null,
        string? clienteId = null,
        bool guardarAlCobrar = false,
        DateTime? creadoEn = null,
        TimeSpan? vigencia = null)
    {
        var tarjeta = new TarjetaValidada(
            numero,
            ReglasTarjeta.DetectarMarca(numero),
            mesExp,
            anioExp ?? DateTime.UtcNow.Year + 3,
            cvv,
            Titular);

        return TokenPago.DesdeTarjetaNueva(
            id,
            clienteId,
            tarjeta,
            Cifrador.Cifrar(new SecretoTarjeta(numero, cvv), id),
            guardarAlCobrar,
            creadoEn ?? DateTime.UtcNow,
            vigencia ?? TimeSpan.FromMinutes(30));
    }

    /// <summary>Token de una tarjeta guardada: solo el CVV va cifrado.</summary>
    public static TokenPago TokenGuardada(
        string id,
        TarjetaGuardada tarjeta,
        string cvv = "123",
        DateTime? creadoEn = null,
        TimeSpan? vigencia = null) =>
        TokenPago.DesdeTarjetaGuardada(
            id,
            tarjeta.ClienteId,
            tarjeta,
            Cifrador.Cifrar(new SecretoTarjeta(null, cvv), id),
            creadoEn ?? DateTime.UtcNow,
            vigencia ?? TimeSpan.FromMinutes(30));

    /// <summary>Un token ya vencido (creado hace 31 minutos con 30 de vigencia).</summary>
    public static TokenPago TokenVencido(string id, string? clienteId = null) =>
        TokenNuevo(id, clienteId: clienteId, creadoEn: DateTime.UtcNow.AddMinutes(-31), vigencia: TimeSpan.FromMinutes(30));

    /// <summary>Guarda los tokens en la base InMemory del contexto.</summary>
    public static async Task SembrarAsync(PagosDbContext contexto, params TokenPago[] tokens)
    {
        contexto.TokensPago.AddRange(tokens);
        await contexto.SaveChangesAsync();
    }
}
