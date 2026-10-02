using System.Text.Json;
using SgoLogic.Mocks.PayKlik;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>
/// Validacion que hace el mock de PayKlik de la tarjeta que llega en un cargo (A6):
/// formato y Luhn, CVV por marca, vencimiento y titular. El mock solo recuerda
/// metadatos no sensibles y nunca imprime los datos recibidos.
/// </summary>
public sealed class CargoMockTests
{
    private static readonly DateTime Ahora = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static DatosTarjetaCargo Valida(
        string? numero = "4242424242424242",
        string? cvv = "123",
        int? mes = 12,
        int? anio = 2030,
        string? titular = "Ana Lopez") => new(numero, mes, anio, cvv, titular);

    [Theory]
    [InlineData("4242424242424242", "VISA", "123", "4242")]
    [InlineData("4222222222222", "VISA", "123", "2222")]
    [InlineData("5555555555554444", "MASTERCARD", "123", "4444")]
    [InlineData("2223003122003222", "MASTERCARD", "123", "3222")]
    [InlineData("378282246310005", "AMEX", "1234", "0005")]
    [InlineData("4242 4242 4242 4242", "VISA", "123", "4242")]
    public void Una_tarjeta_valida_devuelve_ultimos_cuatro_y_la_marca_deducida_del_BIN(
        string numero, string marca, string cvv, string ultimosCuatro)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(numero, cvv), Ahora);

        Assert.True(resultado.EsValida);
        Assert.Equal(marca, resultado.Tarjeta!.Marca);
        Assert.Equal(ultimosCuatro, resultado.Tarjeta.UltimosCuatro);
    }

    [Theory]
    [InlineData("4242424242424241")]
    [InlineData("5555555555554445")]
    public void Un_numero_que_no_pasa_Luhn_se_rechaza(string numero)
    {
        var error = Assert.Single(ValidadorTarjeta.Validar(Valida(numero), Ahora).Errores);

        Assert.Equal("numero", error.Campo);
        Assert.Contains("Luhn", error.Motivo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4242abcd42424242")]
    [InlineData("424242424242")]
    [InlineData("6011111111111117")]
    public void Un_numero_vacio_con_letras_de_longitud_incorrecta_o_de_marca_no_soportada_se_rechaza(string numero)
    {
        Assert.Contains(ValidadorTarjeta.Validar(Valida(numero), Ahora).Errores, e => e.Campo == "numero");
    }

    [Theory]
    [InlineData("4242424242424242", "1234")]
    [InlineData("378282246310005", "123")]
    [InlineData("4242424242424242", "")]
    public void El_CVV_debe_tener_el_largo_de_la_marca(string numero, string cvv)
    {
        Assert.Contains(ValidadorTarjeta.Validar(Valida(numero, cvv), Ahora).Errores, e => e.Campo == "cvv");
    }

    [Theory]
    [InlineData(8, 2026)]
    [InlineData(12, 2025)]
    public void Una_expiracion_pasada_se_rechaza(int mes, int anio)
    {
        Assert.Contains(ValidadorTarjeta.Validar(Valida(mes: mes, anio: anio), Ahora).Errores, e => e.Campo == "expiracion");
    }

    [Theory]
    [InlineData(9, 2026)]
    [InlineData(12, 30)]
    public void Una_expiracion_del_mes_actual_o_futura_se_acepta(int mes, int anio)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(mes: mes, anio: anio), Ahora);

        Assert.True(resultado.EsValida);
        Assert.Equal(anio < 100 ? 2000 + anio : anio, resultado.Tarjeta!.AnioExpiracion);
    }

    [Fact]
    public void Sin_titular_se_rechaza()
    {
        Assert.Contains(ValidadorTarjeta.Validar(Valida(titular: " "), Ahora).Errores, e => e.Campo == "titular");
    }

    [Fact]
    public void Los_errores_nunca_repiten_el_numero_ni_el_CVV_recibidos()
    {
        const string numero = "4242424242424241";
        const string cvv = "98765";

        var resultado = ValidadorTarjeta.Validar(Valida(numero, cvv, 1, 2020), Ahora);

        var texto = JsonSerializer.Serialize(resultado.Errores);
        Assert.DoesNotContain(numero, texto);
        Assert.DoesNotContain(cvv, texto);
    }

    [Fact]
    public void La_peticion_de_cargo_y_la_tarjeta_impresas_no_filtran_el_PAN_ni_el_CVV()
    {
        var tarjeta = Valida(cvv: "731");
        var peticion = new PeticionCargo("ORD-2026-000001", "100.00", "GTQ", "TARJETA", tarjeta, Guardar: true);
        var guardada = new PeticionCargo("ORD-2026-000002", "100.00", "GTQ", "TARJETA", ReferenciaTarjeta: "card_pk_x", Cvv: "4321");

        foreach (var impresa in new[] { tarjeta.ToString(), peticion.ToString(), guardada.ToString() })
        {
            Assert.DoesNotContain("4242424242424242", impresa);
            Assert.DoesNotContain("731", impresa);
            Assert.DoesNotContain("4321", impresa);
        }
    }

    [Fact]
    public void El_resumen_que_guarda_el_mock_no_tiene_PAN_ni_CVV()
    {
        foreach (var propiedad in typeof(ResumenTarjeta).GetProperties())
        {
            var nombre = propiedad.Name.ToLowerInvariant();
            Assert.DoesNotContain("cvv", nombre);
            Assert.DoesNotContain("numero", nombre);
        }
    }

    [Theory]
    [InlineData("4242424242424242", "VISA")]
    [InlineData("5105105105105100", "MASTERCARD")]
    [InlineData("2720999999999999", "MASTERCARD")]
    [InlineData("371449635398431", "AMEX")]
    public void La_marca_se_deduce_del_BIN(string numero, string marca)
    {
        Assert.Equal(marca, ValidadorTarjeta.DeducirMarca(numero));
    }

    [Theory]
    [InlineData("6011111111111117")]
    [InlineData("3530111333300000")]
    public void Un_BIN_de_otra_marca_no_se_deduce(string numero)
    {
        Assert.Null(ValidadorTarjeta.DeducirMarca(numero));
    }
}
