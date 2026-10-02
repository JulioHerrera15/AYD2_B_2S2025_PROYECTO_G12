using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tokens;

/// <summary>
/// Lo que se cifra dentro de un <see cref="TokenPago" />: el PAN y el CVV de una
/// tarjeta nueva, o solo el CVV si el token sale de una tarjeta guardada
/// (<see cref="Numero" /> null). Solo vive en memoria mientras se tokeniza o se
/// cobra; <see cref="ToString" /> no imprime nada sensible.
/// </summary>
public sealed record SecretoTarjeta(string? Numero, string Cvv)
{
    public override string ToString() => nameof(SecretoTarjeta) + " { datos de tarjeta omitidos }";
}

/// <summary>
/// Cifrado autenticado de los datos de la boveda transitoria. <paramref name="datoAsociado" />
/// es el id del token: el blob queda atado a su fila y no se puede descifrar si se
/// copia a otra.
/// </summary>
public interface ICifradorDatosTarjeta
{
    DatosCifradosTarjeta Cifrar(SecretoTarjeta secreto, string datoAsociado);

    /// <summary>
    /// Lanza <see cref="System.Security.Cryptography.CryptographicException" /> si el
    /// tag no coincide (datos alterados, otra clave u otro token).
    /// </summary>
    SecretoTarjeta Descifrar(DatosCifradosTarjeta datos, string datoAsociado);
}
