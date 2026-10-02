using SgoLogic.IntegracionPos.Worker.Application.Pos;

namespace SgoLogic.IntegracionPos.Tests;

public sealed class ServicioMapeoPosTests
{
    [Fact]
    public void Resuelve_TDA_014_a_T14()
    {
        var servicio = new ServicioMapeoPos(defaultTienda: "T01");

        var resultado = servicio.ResolverPosInstanciaId("TDA-014");

        Assert.Equal("T14", resultado);
    }

    [Fact]
    public void Resuelve_T05_directo()
    {
        var servicio = new ServicioMapeoPos(defaultTienda: "T01");

        var resultado = servicio.ResolverPosInstanciaId("T05");

        Assert.Equal("T05", resultado);
    }

    [Fact]
    public void Usa_default_cuando_tienda_es_nula()
    {
        var servicio = new ServicioMapeoPos(defaultTienda: "T01");

        var resultado = servicio.ResolverPosInstanciaId(null);

        Assert.Equal("T01", resultado);
    }

    [Theory]
    [InlineData("TIENDA-01-CENTRAL", "T01")]
    [InlineData("TIENDA-02-MIRAFLORES", "T02")]
    [InlineData("TIENDA-03-PORTALES", "T03")]
    [InlineData("TIENDA-04-PRADERA", "T04")]
    [InlineData("TIENDA-05-OAKLAND", "T05")]
    [InlineData("TIENDA-32-SALAMA", "T32")]
    [InlineData("TIENDA-12", "T12")]
    [InlineData("TDA-08", "T08")]
    [InlineData("T1", "T01")]
    [InlineData("32", "T32")]
    [InlineData("UBICACION-DESCONOCIDA", "T01")]
    public void Mapea_sucursales_frontend_y_formatos_pos_correctamente(string input, string esperado)
    {
        var servicio = new ServicioMapeoPos(defaultTienda: "T01");

        var resultado = servicio.ResolverPosInstanciaId(input);

        Assert.Equal(esperado, resultado);
    }
}
