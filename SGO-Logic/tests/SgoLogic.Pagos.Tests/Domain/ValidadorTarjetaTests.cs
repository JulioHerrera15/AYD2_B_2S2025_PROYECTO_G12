using SgoLogic.Pagos.Api.Domain;
using Xunit;

namespace SgoLogic.Pagos.Tests.Domain;

/// <summary>
/// A1: reglas de validacion de una tarjeta nueva con errores por campo. Numeros de
/// prueba publicos de las marcas (pasan Luhn).
/// </summary>
public sealed class ValidadorTarjetaTests
{
    private static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static DatosTarjeta Datos(
        string numero = "4242424242424242",
        int? mes = 12,
        int? anio = 2029,
        string cvv = "123",
        string titular = "Ana Lopez") => new(numero, mes, anio, cvv, titular);

    private static IReadOnlyDictionary<string, string> Errores(DatosTarjeta datos) =>
        ValidadorTarjeta.Validar(datos, Ahora).Errores;

    // ── Luhn ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4242424242424242")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    public void Numero_que_pasa_Luhn_es_valido(string numero)
    {
        var resultado = ValidadorTarjeta.Validar(Datos(numero), Ahora);

        Assert.True(resultado.EsValida);
        Assert.Equal(MarcaTarjeta.VISA, resultado.Tarjeta!.Marca);
        Assert.Equal(numero[^4..], resultado.Tarjeta.Ultimos4);
    }

    [Theory]
    [InlineData("4242424242424241")]
    [InlineData("4111111111111112")]
    public void Numero_que_no_pasa_Luhn_es_invalido(string numero)
    {
        var errores = Errores(Datos(numero));

        Assert.True(errores.ContainsKey("numero"));
        Assert.Single(errores);
    }

    [Fact]
    public void Numero_con_letras_es_invalido_y_el_mensaje_no_repite_el_valor()
    {
        var errores = Errores(Datos("4242abcd42424242"));

        Assert.Contains("digitos", errores["numero"]);
        Assert.DoesNotContain("4242", errores["numero"]);
    }

    // ── Longitud por marca ────────────────────────────────────────────────────

    [Theory]
    [InlineData("4222222222222", MarcaTarjeta.VISA)]          // Visa de 13 digitos
    [InlineData("4242424242424242", MarcaTarjeta.VISA)]       // Visa de 16
    [InlineData("4012888888881881", MarcaTarjeta.VISA)]
    [InlineData("5555555555554444", MarcaTarjeta.MASTERCARD)] // Mastercard 51-55
    [InlineData("2223003122003222", MarcaTarjeta.MASTERCARD)] // Mastercard rango 2221-2720
    [InlineData("2720992720992711", MarcaTarjeta.MASTERCARD)] // limite superior del rango 2-series
    [InlineData("378282246310005", MarcaTarjeta.AMEX)]        // Amex de 15
    public void Longitud_correcta_para_la_marca_es_valida(string numero, MarcaTarjeta marca)
    {
        var cvv = marca == MarcaTarjeta.AMEX ? "1234" : "123";
        var resultado = ValidadorTarjeta.Validar(Datos(numero, cvv: cvv), Ahora);

        Assert.True(resultado.EsValida, string.Join(" ", resultado.Errores.Values));
        Assert.Equal(marca, resultado.Tarjeta!.Marca);
    }

    [Theory]
    [InlineData("42424242424242")]      // Visa de 14 digitos
    [InlineData("555555555555444")]     // Mastercard de 15
    [InlineData("3782822463100050")]    // Amex de 16
    public void Longitud_incorrecta_para_la_marca_es_invalida(string numero)
    {
        var errores = Errores(Datos(numero, cvv: numero.StartsWith('3') ? "1234" : "123"));

        Assert.Contains("Longitud", errores["numero"]);
    }

    [Theory]
    [InlineData("2220990000000002")]    // justo antes del rango 2221-2720
    [InlineData("6011111111111117")]    // Discover: marca no soportada
    public void Prefijo_sin_marca_soportada_es_invalido(string numero)
    {
        Assert.Contains("Marca no soportada", Errores(Datos(numero))["numero"]);
    }

    // ── Vencimiento ───────────────────────────────────────────────────────────

    [Fact]
    public void Tarjeta_que_vence_en_el_mes_actual_es_valida_hasta_fin_de_mes()
    {
        Assert.True(ValidadorTarjeta.Validar(Datos(mes: 10, anio: 2026), Ahora).EsValida);
        Assert.True(ValidadorTarjeta.Validar(Datos(mes: 10, anio: 26), Ahora).EsValida);
    }

    [Fact]
    public void Tarjeta_que_vencio_el_mes_anterior_es_invalida()
    {
        var errores = Errores(Datos(mes: 9, anio: 2026));

        Assert.Equal("La tarjeta esta vencida.", errores["anioExp"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Mes_fuera_de_1_a_12_es_invalido(int mes)
    {
        Assert.True(Errores(Datos(mes: mes)).ContainsKey("mesExp"));
    }

    [Fact]
    public void Sin_mes_ni_anio_reporta_ambos_campos()
    {
        var errores = Errores(Datos(mes: null, anio: null));

        Assert.True(errores.ContainsKey("mesExp"));
        Assert.True(errores.ContainsKey("anioExp"));
    }

    // ── CVV por marca ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4242424242424242", "123", true)]
    [InlineData("4242424242424242", "1234", false)]
    [InlineData("5555555555554444", "12", false)]
    [InlineData("378282246310005", "1234", true)]
    [InlineData("378282246310005", "123", false)]
    [InlineData("4242424242424242", "12a", false)]
    public void El_CVV_tiene_3_digitos_o_4_si_es_AMEX(string numero, string cvv, bool valido)
    {
        var errores = Errores(Datos(numero, cvv: cvv));

        Assert.Equal(!valido, errores.ContainsKey("cvv"));
    }

    // ── Titular ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("Ana L0pez")]
    [InlineData("Ana-Lopez")]
    [InlineData("Ana_Lopez!")]
    public void Titular_invalido_se_reporta_en_su_campo(string titular)
    {
        Assert.True(Errores(Datos(titular: titular)).ContainsKey("titular"));
    }

    [Fact]
    public void Titular_de_mas_de_60_caracteres_es_invalido()
    {
        Assert.True(Errores(Datos(titular: new string('a', 61))).ContainsKey("titular"));
    }

    [Theory]
    [InlineData("José Pérez Muñoz")]
    [InlineData("  Ana   Lopez  ")]
    [InlineData("Li")]
    public void Titular_con_letras_acentos_y_espacios_es_valido(string titular)
    {
        var resultado = ValidadorTarjeta.Validar(Datos(titular: titular), Ahora);

        Assert.True(resultado.EsValida);
        Assert.Equal(string.Join(' ', titular.Split(' ', StringSplitOptions.RemoveEmptyEntries)), resultado.Tarjeta!.Titular);
    }

    // ── Varios errores a la vez y sin datos sensibles al imprimir ─────────────

    [Fact]
    public void Reporta_un_error_por_cada_campo_invalido()
    {
        var errores = Errores(new DatosTarjeta("1234", 13, 2020, "", ""));

        Assert.Equal(new[] { "cvv", "mesExp", "numero", "titular" }, errores.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Ni_la_solicitud_ni_la_tarjeta_validada_imprimen_el_numero_o_el_cvv()
    {
        var datos = Datos(cvv: "987");
        var tarjeta = ValidadorTarjeta.Validar(datos, Ahora).Tarjeta!;

        Assert.DoesNotContain("4242424242424242", datos.ToString());
        Assert.DoesNotContain("987", datos.ToString());
        Assert.DoesNotContain("4242424242424242", tarjeta.ToString());
        Assert.DoesNotContain("987", tarjeta.ToString());
    }
}
