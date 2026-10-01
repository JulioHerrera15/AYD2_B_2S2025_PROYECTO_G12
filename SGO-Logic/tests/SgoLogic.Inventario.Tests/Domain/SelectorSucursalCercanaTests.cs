using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;
using Xunit;

namespace SgoLogic.Inventario.Tests.Domain;

/// <summary>
/// Tienda mas cercana con stock. Las coordenadas son las del catalogo de sucursales
/// (inventario.sucursal): desde T01 Central, Fontabella esta a 0.4 km, Oakland a
/// 0.65 km y Xela a mas de 100 km.
/// </summary>
public sealed class SelectorSucursalCercanaTests
{
    private const string Sku = "PROD-102";

    private static readonly Sucursal Central = new("TIENDA-01-CENTRAL", "MegaStore Central", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", 14.6010, -90.5130);
    private static readonly Sucursal Fontabella = new("TIENDA-09-FONTABELLA", "MegaStore Plaza Fontabella", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", 14.6040, -90.5150);
    private static readonly Sucursal Oakland = new("TIENDA-05-OAKLAND", "MegaStore Oakland Mall", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", 14.5985, -90.5075);
    private static readonly Sucursal Xela = new("TIENDA-17-XELA", "MegaStore Utz Ulew", TipoUbicacion.TIENDA, "Quetzaltenango", "Quetzaltenango", 14.8460, -91.5300);
    private static readonly Sucursal CentroDistribucion = new("CD-CENTRAL", "Centro de Distribución Central", TipoUbicacion.CENTRO_DISTRIBUCION, "Guatemala", "Guatemala", 14.5860, -90.5420);

    private static readonly Sucursal[] Catalogo = { Central, Fontabella, Oakland, Xela, CentroDistribucion };

    private static Existencia En(Sucursal sucursal, int cantidad, string sku = Sku) =>
        new(sku, sucursal.Id, sucursal.TipoUbicacion, cantidad);

    private static LineaReservada[] Pedido(int cantidad = 1) => new[] { new LineaReservada(Sku, cantidad) };

    [Fact]
    public void Elige_la_tienda_mas_cercana_con_stock_y_omite_la_mas_cercana_que_tiene_cero()
    {
        var existencias = new[]
        {
            En(Central, 0),
            En(Fontabella, 0),
            En(Oakland, 2),
            En(Xela, 2),
            En(CentroDistribucion, 50)
        };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, Central.Id);

        Assert.NotNull(cercana);
        Assert.Equal(Oakland.Id, cercana!.Sucursal.Id);
        Assert.Equal(2, cercana.Disponible);
        Assert.NotNull(cercana.DistanciaKm);
        Assert.InRange(cercana.DistanciaKm!.Value, 0.5, 0.8);
    }

    [Fact]
    public void Devuelve_null_si_ninguna_tienda_tiene_stock_aunque_un_centro_de_distribucion_si()
    {
        var existencias = new[]
        {
            En(Central, 0),
            En(Fontabella, 0),
            En(Oakland, 0),
            En(CentroDistribucion, 10)
        };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, Central.Id);

        Assert.Null(cercana);
    }

    [Fact]
    public void Excluye_la_ubicacion_de_origen_aunque_tenga_stock()
    {
        var existencias = new[] { En(Central, 5), En(Xela, 1) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, Central.Id);

        Assert.Equal(Xela.Id, cercana?.Sucursal.Id);
    }

    [Fact]
    public void Exige_la_cantidad_pedida_en_una_misma_tienda()
    {
        var existencias = new[] { En(Fontabella, 1), En(Oakland, 1), En(Xela, 3) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(cantidad: 3), existencias, Catalogo, Central.Id);

        Assert.Equal(Xela.Id, cercana?.Sucursal.Id);
    }

    [Fact]
    public void Sin_origen_conocido_ordena_por_menor_sobrante()
    {
        var existencias = new[] { En(Fontabella, 9), En(Oakland, 2), En(Xela, 4) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, "TIENDA-DESCONOCIDA");

        Assert.Equal(Oakland.Id, cercana?.Sucursal.Id);
        Assert.Null(cercana?.DistanciaKm);
    }
}
