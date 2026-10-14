using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;
using Xunit;

namespace SgoLogic.Inventario.Tests.Infrastructure;

/// <summary>
/// Catalogo de sucursales que carga la migracion: 34 ubicaciones repartidas en
/// Guatemala, El Salvador y Costa Rica, con los mismos identificadores que usan
/// existencias, reservas, ordenes, despacho, POS y seeds.
/// </summary>
public sealed class SucursalesSemillaTests
{
    /// <summary>Identificadores heredados; cambiar uno rompe las referencias en las demas tablas y seeds.</summary>
    private static readonly string[] IdsEsperados =
    {
        "CD-CENTRAL", "CD-01",
        "TIENDA-01-CENTRAL", "TIENDA-02-MIRAFLORES", "TIENDA-03-PORTALES", "TIENDA-04-PRADERA",
        "TIENDA-05-OAKLAND", "TIENDA-06-NARANJO", "TIENDA-07-CAYALA", "TIENDA-08-ROOSEVELT",
        "TIENDA-09-FONTABELLA", "TIENDA-10-METRONORTE", "TIENDA-11-MAJADAS", "TIENDA-12-AMERICAS",
        "TIENDA-13-SANCRISTOBAL", "TIENDA-14-VILLANUEVA", "TIENDA-15-CARRETERA", "TIENDA-16-CENTRALNORTE",
        "TIENDA-17-XELA", "TIENDA-18-INTERPLAZA", "TIENDA-19-ANTIGUA", "TIENDA-20-ESCUINTLA",
        "TIENDA-21-MAZATENANGO", "TIENDA-22-COBAN", "TIENDA-23-HUEHUETENANGO", "TIENDA-24-CHIMALTENANGO",
        "TIENDA-25-ZACAPA", "TIENDA-26-CHIQUIMULA", "TIENDA-27-PUERTOBARRIOS", "TIENDA-28-PETEN",
        "TIENDA-29-RETALHULEU", "TIENDA-30-COATEPEQUE", "TIENDA-31-JUTIAPA", "TIENDA-32-SALAMA"
    };

    [Fact]
    public void Tiene_34_ubicaciones_con_los_mismos_identificadores()
    {
        var ids = SucursalesSemilla.Todas.Select(sucursal => sucursal.Id).ToList();

        Assert.Equal(34, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(IdsEsperados.OrderBy(id => id, StringComparer.Ordinal), ids.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Reparte_las_ubicaciones_entre_los_tres_paises()
    {
        var porPais = SucursalesSemilla.Todas
            .GroupBy(sucursal => sucursal.Pais)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.ToList());

        Assert.Equal(3, porPais.Count);

        var guatemala = porPais[SucursalesSemilla.Guatemala];
        Assert.Equal(18, guatemala.Count);
        Assert.Equal(2, guatemala.Count(sucursal => sucursal.TipoUbicacion == TipoUbicacion.CENTRO_DISTRIBUCION));
        Assert.Equal(16, guatemala.Count(sucursal => sucursal.TipoUbicacion == TipoUbicacion.TIENDA));

        Assert.Equal(8, porPais[SucursalesSemilla.ElSalvador].Count);
        Assert.Equal(8, porPais[SucursalesSemilla.CostaRica].Count);
        Assert.All(porPais[SucursalesSemilla.ElSalvador], sucursal => Assert.Equal(TipoUbicacion.TIENDA, sucursal.TipoUbicacion));
        Assert.All(porPais[SucursalesSemilla.CostaRica], sucursal => Assert.Equal(TipoUbicacion.TIENDA, sucursal.TipoUbicacion));
    }

    [Theory]
    [InlineData(1, 16, SucursalesSemilla.Guatemala)]
    [InlineData(17, 24, SucursalesSemilla.ElSalvador)]
    [InlineData(25, 32, SucursalesSemilla.CostaRica)]
    public void Cada_rango_de_tiendas_esta_en_su_pais(int desde, int hasta, string pais)
    {
        for (var numero = desde; numero <= hasta; numero++)
        {
            var prefijo = $"TIENDA-{numero:D2}-";
            var tienda = Assert.Single(SucursalesSemilla.Todas, sucursal => sucursal.Id.StartsWith(prefijo, StringComparison.Ordinal));

            Assert.Equal(pais, tienda.Pais);
            Assert.EndsWith($"(T{numero:D2})", tienda.Nombre, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Las_coordenadas_quedan_dentro_del_pais_de_cada_sucursal()
    {
        // Cajas aproximadas de cada pais (latitud y longitud minimas y maximas).
        var cajas = new Dictionary<string, (double LatMin, double LatMax, double LonMin, double LonMax)>
        {
            [SucursalesSemilla.Guatemala] = (13.7, 17.9, -92.3, -88.2),
            [SucursalesSemilla.ElSalvador] = (13.1, 14.5, -90.2, -87.6),
            [SucursalesSemilla.CostaRica] = (8.0, 11.3, -86.0, -82.5)
        };

        Assert.All(SucursalesSemilla.Todas, sucursal =>
        {
            Assert.True(sucursal.TieneCoordenadas, $"{sucursal.Id} no tiene coordenadas.");
            Assert.False(string.IsNullOrWhiteSpace(sucursal.Departamento), $"{sucursal.Id} no tiene departamento.");
            Assert.False(string.IsNullOrWhiteSpace(sucursal.Municipio), $"{sucursal.Id} no tiene municipio.");

            var caja = cajas[sucursal.Pais];
            Assert.InRange(sucursal.Latitud!.Value, caja.LatMin, caja.LatMax);
            Assert.InRange(sucursal.Longitud!.Value, caja.LonMin, caja.LonMax);
        });
    }
}
