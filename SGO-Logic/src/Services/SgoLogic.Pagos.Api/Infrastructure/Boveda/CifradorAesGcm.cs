using System.Security.Cryptography;
using System.Text;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Boveda;

/// <summary>
/// AES-256-GCM con la clave de PAGOS_CLAVE_CIFRADO. Nonce aleatorio de 12 bytes por
/// cifrado y tag de 16. El texto plano (numero y CVV separados por un salto de linea)
/// se borra de memoria en cuanto se cifra o se descifra.
///
/// El constructor valida la clave: si falta o no son 32 bytes en base64 lanza, y como
/// Program.cs lo construye al arrancar, el servicio no levanta con una clave invalida.
/// </summary>
public sealed class CifradorAesGcm : ICifradorDatosTarjeta
{
    public const int LargoClave = 32;
    private const int LargoNonce = 12;
    private const int LargoTag = 16;
    private const char Separador = '\n';

    private readonly byte[] _clave;

    public CifradorAesGcm(string? claveBase64)
    {
        if (string.IsNullOrWhiteSpace(claveBase64))
        {
            throw new InvalidOperationException(
                "Boveda:ClaveCifrado (PAGOS_CLAVE_CIFRADO) no esta configurada. Debe ser una clave de 32 bytes en base64.");
        }

        byte[] clave;
        try
        {
            clave = Convert.FromBase64String(claveBase64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Boveda:ClaveCifrado (PAGOS_CLAVE_CIFRADO) no es base64 valido.");
        }

        if (clave.Length != LargoClave)
        {
            CryptographicOperations.ZeroMemory(clave);
            throw new InvalidOperationException(
                $"Boveda:ClaveCifrado (PAGOS_CLAVE_CIFRADO) debe tener {LargoClave} bytes; tiene {clave.Length}.");
        }

        _clave = clave;
    }

    public DatosCifradosTarjeta Cifrar(SecretoTarjeta secreto, string datoAsociado)
    {
        ArgumentNullException.ThrowIfNull(secreto);

        var plano = Encoding.UTF8.GetBytes((secreto.Numero ?? string.Empty) + Separador + secreto.Cvv);
        var asociado = Encoding.UTF8.GetBytes(datoAsociado);
        var nonce = RandomNumberGenerator.GetBytes(LargoNonce);
        var cifrado = new byte[plano.Length];
        var tag = new byte[LargoTag];

        try
        {
            using var aes = new AesGcm(_clave, LargoTag);
            aes.Encrypt(nonce, plano, cifrado, tag, asociado);
            return new DatosCifradosTarjeta(cifrado, nonce, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plano);
        }
    }

    public SecretoTarjeta Descifrar(DatosCifradosTarjeta datos, string datoAsociado)
    {
        ArgumentNullException.ThrowIfNull(datos);

        if (datos.Nonce.Length != LargoNonce || datos.Tag.Length != LargoTag)
        {
            throw new CryptographicException("Los datos cifrados del token no tienen el formato esperado.");
        }

        var plano = new byte[datos.Cifrado.Length];
        try
        {
            using var aes = new AesGcm(_clave, LargoTag);
            aes.Decrypt(datos.Nonce, datos.Cifrado, datos.Tag, plano, Encoding.UTF8.GetBytes(datoAsociado));

            var texto = Encoding.UTF8.GetString(plano);
            var corte = texto.IndexOf(Separador);
            if (corte < 0)
            {
                throw new CryptographicException("Los datos cifrados del token no tienen el formato esperado.");
            }

            var numero = texto[..corte];
            return new SecretoTarjeta(numero.Length == 0 ? null : numero, texto[(corte + 1)..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plano);
        }
    }
}
