using System.Security.Cryptography;
using System.Text;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Boveda;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>A2: cifrado AES-GCM de la boveda transitoria.</summary>
public sealed class CifradorAesGcmTests
{
    private const string Token = "tok_0123456789abcdef0123456789abcdef";

    private static readonly CifradorAesGcm Cifrador = BovedaPruebas.Cifrador;

    [Fact]
    public void Cifrar_y_descifrar_devuelve_el_mismo_PAN_y_CVV()
    {
        var datos = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);

        var secreto = Cifrador.Descifrar(datos, Token);

        Assert.Equal("4242424242424242", secreto.Numero);
        Assert.Equal("731", secreto.Cvv);
    }

    [Fact]
    public void Solo_CVV_de_una_tarjeta_guardada_hace_ida_y_vuelta_sin_numero()
    {
        var datos = Cifrador.Cifrar(new SecretoTarjeta(null, "4321"), Token);

        var secreto = Cifrador.Descifrar(datos, Token);

        Assert.Null(secreto.Numero);
        Assert.Equal("4321", secreto.Cvv);
    }

    [Fact]
    public void El_texto_cifrado_no_contiene_el_PAN_y_cada_cifrado_usa_un_nonce_distinto()
    {
        var primero = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);
        var segundo = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);

        Assert.DoesNotContain("4242424242424242", Encoding.UTF8.GetString(primero.Cifrado));
        Assert.Equal(12, primero.Nonce.Length);
        Assert.Equal(16, primero.Tag.Length);
        Assert.NotEqual(primero.Nonce, segundo.Nonce);
        Assert.NotEqual(primero.Cifrado, segundo.Cifrado);
    }

    [Fact]
    public void Un_Tag_alterado_se_detecta_al_descifrar()
    {
        var datos = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);
        var tagAlterado = (byte[])datos.Tag.Clone();
        tagAlterado[0] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(
            () => Cifrador.Descifrar(datos with { Tag = tagAlterado }, Token));
    }

    [Fact]
    public void Un_texto_cifrado_alterado_o_copiado_a_otro_token_se_detecta()
    {
        var datos = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);
        var cifradoAlterado = (byte[])datos.Cifrado.Clone();
        cifradoAlterado[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(
            () => Cifrador.Descifrar(datos with { Cifrado = cifradoAlterado }, Token));
        Assert.ThrowsAny<CryptographicException>(
            () => Cifrador.Descifrar(datos, "tok_otro_token"));
    }

    [Fact]
    public void Otra_clave_no_puede_descifrar()
    {
        var datos = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);
        var otra = new CifradorAesGcm(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.ThrowsAny<CryptographicException>(() => otra.Descifrar(datos, Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-base64!!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")] // 16 bytes: AES-128, no se acepta
    public void Sin_clave_o_con_una_clave_invalida_el_servicio_no_arranca(string? clave)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new CifradorAesGcm(clave));
        Assert.Contains("PAGOS_CLAVE_CIFRADO", ex.Message);
    }

    [Fact]
    public void Los_datos_cifrados_no_se_imprimen()
    {
        var datos = Cifrador.Cifrar(new SecretoTarjeta("4242424242424242", "731"), Token);

        Assert.DoesNotContain(Convert.ToBase64String(datos.Cifrado), datos.ToString());
        Assert.DoesNotContain("4242424242424242", new SecretoTarjeta("4242424242424242", "731").ToString());
    }
}
