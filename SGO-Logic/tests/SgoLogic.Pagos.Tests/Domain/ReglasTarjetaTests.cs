using SgoLogic.Pagos.Api.Domain;
using Xunit;

namespace SgoLogic.Pagos.Tests.Domain;

/// <summary>Reglas puras de las tarjetas guardadas: marca, CVV, enmascarado y vencimiento.</summary>
public sealed class ReglasTarjetaTests
{
    [Theory]
    [InlineData("4111111111111111", MarcaTarjeta.VISA)]
    [InlineData("4", MarcaTarjeta.VISA)]
    [InlineData("5100000000000000", MarcaTarjeta.MASTERCARD)]
    [InlineData("5555555555554444", MarcaTarjeta.MASTERCARD)]
    [InlineData("2221000000000009", MarcaTarjeta.MASTERCARD)]
    [InlineData("2500000000000001", MarcaTarjeta.MASTERCARD)]
    [InlineData("2720999999999999", MarcaTarjeta.MASTERCARD)]
    [InlineData("378282246310005", MarcaTarjeta.AMEX)]
    [InlineData("3400 0000 0000 009", MarcaTarjeta.AMEX)]
    public void Detecta_la_marca_por_prefijo(string numero, MarcaTarjeta esperada)
    {
        Assert.Equal(esperada, ReglasTarjeta.DetectarMarca(numero));
    }

    [Theory]
    [InlineData("5000000000000000")]
    [InlineData("5600000000000000")]
    [InlineData("2220999999999999")]
    [InlineData("2721000000000000")]
    [InlineData("3530111333300000")]
    [InlineData("6011111111111117")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_prefijo_fuera_de_los_rangos_es_OTRA(string? numero)
    {
        Assert.Equal(MarcaTarjeta.OTRA, ReglasTarjeta.DetectarMarca(numero));
    }

    [Fact]
    public void Enmascara_con_los_ultimos_cuatro_digitos()
    {
        Assert.Equal("**** **** **** 1234", ReglasTarjeta.Enmascarar("1234"));
    }

    [Theory]
    [InlineData(9, 2026, false)]
    [InlineData(10, 2026, false)]
    [InlineData(12, 2026, false)]
    [InlineData(1, 2027, false)]
    [InlineData(8, 2026, true)]
    [InlineData(12, 2025, true)]
    public void Vencida_segun_mes_y_anio(int mes, int anio, bool vencida)
    {
        // La tarjeta vale hasta el ultimo dia de su mes de vencimiento.
        var ahora = new DateTime(2026, 9, 30, 23, 0, 0, DateTimeKind.Utc);

        Assert.Equal(vencida, ReglasTarjeta.EstaVencida(mes, anio, ahora));
    }

    [Theory]
    [InlineData(MarcaTarjeta.AMEX, "1234", true)]
    [InlineData(MarcaTarjeta.AMEX, "123", false)]
    [InlineData(MarcaTarjeta.VISA, "123", true)]
    [InlineData(MarcaTarjeta.VISA, "1234", false)]
    [InlineData(MarcaTarjeta.MASTERCARD, "999", true)]
    [InlineData(MarcaTarjeta.MASTERCARD, "12a", false)]
    [InlineData(MarcaTarjeta.OTRA, "123", true)]
    [InlineData(MarcaTarjeta.VISA, null, false)]
    public void El_CVV_de_AMEX_exige_4_digitos_y_el_de_las_demas_3(MarcaTarjeta marca, string? cvv, bool valido)
    {
        Assert.Equal(valido, ReglasTarjeta.CvvValido(marca, cvv));
    }
}
