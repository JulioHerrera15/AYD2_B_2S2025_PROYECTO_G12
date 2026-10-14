using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;
using Xunit;

namespace SgoLogic.Inventario.Tests.Domain;

/// <summary>
/// Tienda mas cercana con stock. Las coordenadas son las del catalogo de sucursales
/// (inventario.sucursal): desde T01 Central, Fontabella esta a 0.4 km, Oakland a
/// 0.65 km y Villa Nueva a unos 10 km. TIENDA-17-XELA (Multiplaza, El Salvador) y
/// TIENDA-25-ZACAPA (Escazu, Costa Rica) son de otro pais y solo se sugieren desde
/// su propio pais o cuando el origen es desconocido.
/// </summary>
public sealed class SelectorSucursalCercanaTests
{
    private const string Sku = "PROD-102";

    private static readonly Sucursal Central = new("TIENDA-01-CENTRAL", "MegaStore Central", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.6010, -90.5130);
    private static readonly Sucursal Fontabella = new("TIENDA-09-FONTABELLA", "MegaStore Plaza Fontabella", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.6040, -90.5150);
    private static readonly Sucursal Oakland = new("TIENDA-05-OAKLAND", "MegaStore Oakland Mall", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.5985, -90.5075);
    private static readonly Sucursal VillaNueva = new("TIENDA-14-VILLANUEVA", "MegaStore Metrocentro — Villa Nueva", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Villa Nueva", 14.5270, -90.5880);
    private static readonly Sucursal Xela = new("TIENDA-17-XELA", "MegaStore Multiplaza — San Salvador", TipoUbicacion.TIENDA, "El Salvador", "La Libertad", "Antiguo Cuscatlán", 13.6785, -89.2520);
    private static readonly Sucursal MetrocentroSanSalvador = new("TIENDA-18-INTERPLAZA", "MegaStore Metrocentro — San Salvador", TipoUbicacion.TIENDA, "El Salvador", "San Salvador", "San Salvador", 13.7055, -89.2130);
    private static readonly Sucursal Escazu = new("TIENDA-25-ZACAPA", "MegaStore Multiplaza Escazú — San José", TipoUbicacion.TIENDA, "Costa Rica", "San José", "Escazú", 9.9390, -84.1440);
    private static readonly Sucursal CentroDistribucion = new("CD-CENTRAL", "Centro de Distribución Central", TipoUbicacion.CENTRO_DISTRIBUCION, "Guatemala", "Guatemala", "Guatemala", 14.5860, -90.5420);

    private static readonly Sucursal[] Catalogo = { Central, Fontabella, Oakland, VillaNueva, Xela, MetrocentroSanSalvador, Escazu, CentroDistribucion };

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
        var existencias = new[] { En(Central, 5), En(VillaNueva, 1) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, Central.Id);

        Assert.Equal(VillaNueva.Id, cercana?.Sucursal.Id);
    }

    [Fact]
    public void Exige_la_cantidad_pedida_en_una_misma_tienda()
    {
        var existencias = new[] { En(Fontabella, 1), En(Oakland, 1), En(VillaNueva, 3) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(cantidad: 3), existencias, Catalogo, Central.Id);

        Assert.Equal(VillaNueva.Id, cercana?.Sucursal.Id);
    }

    [Fact]
    public void Sin_origen_conocido_ordena_por_menor_sobrante()
    {
        var existencias = new[] { En(Fontabella, 9), En(Oakland, 2), En(Xela, 4) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, "TIENDA-DESCONOCIDA");

        Assert.Equal(Oakland.Id, cercana?.Sucursal.Id);
        Assert.Null(cercana?.DistanciaKm);
    }

    [Fact]
    public void No_sugiere_una_tienda_de_otro_pais_aunque_sea_la_unica_con_stock()
    {
        var existencias = new[] { En(Central, 0), En(Oakland, 0), En(Xela, 5), En(Escazu, 5) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, Central.Id);

        Assert.Null(cercana);
    }

    [Fact]
    public void Prefiere_una_tienda_lejana_del_mismo_pais_antes_que_una_mas_cercana_de_otro_pais()
    {
        // Desde Multiplaza (El Salvador): Metrocentro San Salvador esta a unos 4 km;
        // Central (Guatemala) y Escazu (Costa Rica) quedan fuera aunque tengan mas stock.
        var existencias = new[] { En(Xela, 0), En(MetrocentroSanSalvador, 1), En(Central, 50), En(Escazu, 50) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, Xela.Id);

        Assert.NotNull(cercana);
        Assert.Equal(MetrocentroSanSalvador.Id, cercana!.Sucursal.Id);
        Assert.Equal("El Salvador", cercana.Sucursal.Pais);
        Assert.InRange(cercana.DistanciaKm!.Value, 3, 6);
    }

    [Fact]
    public void Desde_un_centro_de_distribucion_solo_considera_tiendas_de_su_pais()
    {
        var existencias = new[] { En(CentroDistribucion, 0), En(Xela, 9), En(VillaNueva, 1) };

        var ordenadas = SelectorSucursalCercana.Ordenar(Pedido(), existencias, Catalogo, CentroDistribucion.Id);

        var unica = Assert.Single(ordenadas);
        Assert.Equal(VillaNueva.Id, unica.Sucursal.Id);
    }

    [Fact]
    public void Sin_origen_conocido_considera_tiendas_de_cualquier_pais()
    {
        var existencias = new[] { En(Central, 0), En(Escazu, 2) };

        var cercana = SelectorSucursalCercana.Elegir(Pedido(), existencias, Catalogo, ubicacionOrigenId: null);

        Assert.Equal(Escazu.Id, cercana?.Sucursal.Id);
    }
}
