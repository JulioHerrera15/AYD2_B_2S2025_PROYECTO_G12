using SgoLogic.Web.Services;
using Xunit;

namespace SgoLogic.ConsultaEstado.Tests.Application;

public sealed class FechaHelperTests
{
    [Fact]
    public void AFechaLocal_ConvierteUtcAHoraGuatemala_Resta6Horas()
    {
        var fechaUtc = new DateTime(2026, 9, 24, 22, 30, 0, DateTimeKind.Utc);
        var fechaLocal = FechaHelper.AFechaLocal(fechaUtc);

        Assert.Equal(2026, fechaLocal.Year);
        Assert.Equal(9, fechaLocal.Month);
        Assert.Equal(24, fechaLocal.Day);
        Assert.Equal(16, fechaLocal.Hour);
        Assert.Equal(30, fechaLocal.Minute);
    }

    [Fact]
    public void AFechaLocal_ManejaCruceDeMedianocheCorrectamente()
    {
        // En UTC ya es 25 de septiembre a las 02:00 AM, pero en Guatemala son las 20:00 PM del 24 de septiembre
        var fechaUtc = new DateTime(2026, 9, 25, 2, 0, 0, DateTimeKind.Utc);
        var fechaLocal = FechaHelper.AFechaLocal(fechaUtc);

        Assert.Equal(2026, fechaLocal.Year);
        Assert.Equal(9, fechaLocal.Month);
        Assert.Equal(24, fechaLocal.Day);
        Assert.Equal(20, fechaLocal.Hour);
        Assert.Equal(0, fechaLocal.Minute);
    }

    [Fact]
    public void Formatear_FechaNulaODefault_RetornaND()
    {
        DateTime? fechaNula = null;
        Assert.Equal("N/D", FechaHelper.Formatear(fechaNula));

        Assert.Equal("N/D", FechaHelper.Formatear(default(DateTime)));
        Assert.Equal("N/D", FechaHelper.Formatear(DateTime.MinValue));
    }

    [Fact]
    public void Formatear_FechaUtc_RetornaFormatoCorrecto()
    {
        var fechaUtc = new DateTime(2026, 9, 24, 18, 15, 0, DateTimeKind.Utc);
        var resultado = FechaHelper.Formatear(fechaUtc);

        // 18:15 UTC - 6h = 12:15 Guatemala
        Assert.Equal("24/09/2026 12:15", resultado);
    }

    [Fact]
    public void FormatearSoloFecha_RetornaFormatoDiaMesAnio()
    {
        var fechaUtc = new DateTime(2026, 9, 25, 1, 0, 0, DateTimeKind.Utc);
        // 01:00 UTC - 6h = 19:00 del dia anterior en Guatemala
        var resultado = FechaHelper.FormatearSoloFecha(fechaUtc);

        Assert.Equal("24/09/2026", resultado);
    }

    [Fact]
    public void FormatearSoloHora_RetornaHoraYMinutoLocal()
    {
        var fechaUtc = new DateTime(2026, 9, 24, 21, 45, 0, DateTimeKind.Utc);
        // 21:45 UTC - 6h = 15:45
        var resultado = FechaHelper.FormatearSoloHora(fechaUtc);

        Assert.Equal("15:45", resultado);
    }

    [Fact]
    public void AFechaLocal_DateTimeKindUnspecified_SeAsumeUtc()
    {
        var fechaSinKind = new DateTime(2026, 9, 24, 20, 0, 0, DateTimeKind.Unspecified);
        var fechaLocal = FechaHelper.AFechaLocal(fechaSinKind);

        Assert.Equal(14, fechaLocal.Hour);
    }
}
