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
}
