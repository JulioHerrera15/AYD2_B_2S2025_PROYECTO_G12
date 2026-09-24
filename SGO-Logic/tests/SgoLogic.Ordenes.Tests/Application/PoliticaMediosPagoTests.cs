using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

public sealed class PoliticaMediosPagoTests
{
    private static PoliticaMediosPago Construir() => new(new MediosPagoPorCanalOptions
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL },
        ["MOVIL"] = new() { MedioPago.TARJETA, MedioPago.BILLETERA_DIGITAL }
    });

    [Fact]
    public void Devuelve_los_medios_configurados_para_el_canal()
    {
        var politica = Construir();

        Assert.Equal(
            new[] { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL },
            politica.MediosHabilitados(Canal.WEB));
    }

    [Fact]
    public void Un_canal_sin_configuracion_no_habilita_ningun_medio()
    {
        var politica = Construir();

        Assert.Empty(politica.MediosHabilitados(Canal.CALL_CENTER));
        Assert.False(politica.EstaHabilitado(Canal.CALL_CENTER, MedioPago.TARJETA));
    }

    [Theory]
    [InlineData(Canal.WEB, MedioPago.TRANSFERENCIA, true)]
    [InlineData(Canal.MOVIL, MedioPago.TRANSFERENCIA, false)]
    [InlineData(Canal.MOVIL, MedioPago.BILLETERA_DIGITAL, true)]
    public void EstaHabilitado_responde_segun_la_configuracion(Canal canal, MedioPago medioPago, bool esperado)
    {
        Assert.Equal(esperado, Construir().EstaHabilitado(canal, medioPago));
    }
}
