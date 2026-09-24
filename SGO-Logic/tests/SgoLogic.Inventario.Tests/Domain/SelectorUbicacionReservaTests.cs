using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;
using Xunit;

namespace SgoLogic.Inventario.Tests.Domain;

public sealed class SelectorUbicacionReservaTests
{
    private static Existencia Existencia(string ubicacion, string sku, int fisica, TipoUbicacion tipo) =>
        new(sku, ubicacion, tipo, fisica);

    private static IReadOnlyList<LineaReservada> Pedido(params (string Sku, int Cantidad)[] lineas) =>
        lineas.Select(linea => new LineaReservada(linea.Sku, linea.Cantidad)).ToList();

    [Fact]
    public void Elige_la_unica_ubicacion_que_cubre_todas_las_lineas()
    {
        var existencias = new[]
        {
            Existencia("CD-01", "A", 10, TipoUbicacion.CENTRO_DISTRIBUCION),
            Existencia("CD-01", "B", 1, TipoUbicacion.CENTRO_DISTRIBUCION),
            Existencia("TDA-9", "A", 10, TipoUbicacion.TIENDA)
        };

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            Pedido(("A", 2), ("B", 1)), existencias, null, MetodoEntrega.DOMICILIO);

        Assert.Equal("CD-01", seleccion.UbicacionId);
        Assert.Null(seleccion.Motivo);
    }

    [Fact]
    public void Prefiere_la_ubicacion_sugerida_por_ordenes_si_es_elegible()
    {
        var existencias = new[]
        {
            Existencia("CD-01", "A", 10, TipoUbicacion.CENTRO_DISTRIBUCION),
            Existencia("TDA-9", "A", 10, TipoUbicacion.TIENDA)
        };

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            Pedido(("A", 1)), existencias, "TDA-9", MetodoEntrega.DOMICILIO);

        Assert.Equal("TDA-9", seleccion.UbicacionId);
    }

    [Fact]
    public void Recogida_en_tienda_prefiere_una_ubicacion_de_tipo_tienda()
    {
        var existencias = new[]
        {
            Existencia("CD-01", "A", 10, TipoUbicacion.CENTRO_DISTRIBUCION),
            Existencia("TDA-9", "A", 10, TipoUbicacion.TIENDA)
        };

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            Pedido(("A", 1)), existencias, null, MetodoEntrega.RECOGIDA_TIENDA);

        Assert.Equal("TDA-9", seleccion.UbicacionId);
    }

    [Fact]
    public void Sin_disponible_suficiente_en_ninguna_ubicacion_devuelve_STOCK_INSUFICIENTE_con_faltantes()
    {
        var existencias = new[]
        {
            Existencia("CD-01", "A", 1, TipoUbicacion.CENTRO_DISTRIBUCION),
            Existencia("TDA-9", "A", 2, TipoUbicacion.TIENDA)
        };

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            Pedido(("A", 5)), existencias, null, MetodoEntrega.DOMICILIO);

        Assert.Null(seleccion.UbicacionId);
        Assert.Equal(MotivoRechazoReserva.STOCK_INSUFICIENTE, seleccion.Motivo);
        var faltante = Assert.Single(seleccion.Faltantes);
        Assert.Equal("A", faltante.Sku);
        Assert.Equal(5, faltante.Solicitado);
        Assert.Equal(2, faltante.Disponible);
    }

    [Fact]
    public void Un_SKU_sin_registro_de_existencia_devuelve_SIN_UBICACION_ELEGIBLE()
    {
        var existencias = new[]
        {
            Existencia("CD-01", "A", 10, TipoUbicacion.CENTRO_DISTRIBUCION)
        };

        var seleccion = SelectorUbicacionReserva.Seleccionar(
            Pedido(("A", 1), ("Z", 1)), existencias, null, MetodoEntrega.DOMICILIO);

        Assert.Null(seleccion.UbicacionId);
        Assert.Equal(MotivoRechazoReserva.SIN_UBICACION_ELEGIBLE, seleccion.Motivo);
    }
}
