using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Catalogo de las 34 ubicaciones que existen en los seeds (scripts/seed/sql y
/// scripts/demo): 2 centros de distribucion y 32 tiendas en tres paises. Lo carga
/// la migracion Sucursales via HasData (la migracion SucursalesPais agrega el pais
/// y corrige las tiendas de El Salvador y Costa Rica), asi que el catalogo existe
/// aunque el seed de datos este apagado.
///
/// Reparto: Guatemala tiene los 2 centros de distribucion y las tiendas T01 a T16,
/// El Salvador T17 a T24 y Costa Rica T25 a T32. Es la fuente unica de verdad de
/// nombre y pais de cada sucursal; el POS y el catalogo web la leen por
/// GET /api/inventario/sucursales.
///
/// Los identificadores son exactamente los de <c>existencia.ubicacion_id</c> y son
/// heredados: TIENDA-17-XELA a TIENDA-32-SALAMA conservan el nombre de una ciudad
/// guatemalteca aunque la tienda este en El Salvador o Costa Rica. No se renombran
/// porque los referencian existencias, reservas, ordenes, despacho, POS y seeds;
/// la ubicacion real es la de <see cref="Sucursal.Pais" />, departamento y municipio.
///
/// Las coordenadas son aproximadas pero reales (centro comercial o casco urbano del
/// municipio), suficientes para ordenar por cercania.
/// </summary>
public static class SucursalesSemilla
{
    public const string Guatemala = "Guatemala";
    public const string ElSalvador = "El Salvador";
    public const string CostaRica = "Costa Rica";

    public static IReadOnlyList<Sucursal> Todas { get; } = new[]
    {
        new Sucursal("CD-CENTRAL", "Centro de Distribución Central — Zona 12", TipoUbicacion.CENTRO_DISTRIBUCION, Guatemala, Guatemala, Guatemala, 14.5860, -90.5420),
        new Sucursal("CD-01", "Centro de Distribución Norte — Zona 18 (CD-01)", TipoUbicacion.CENTRO_DISTRIBUCION, Guatemala, Guatemala, Guatemala, 14.6650, -90.4720),

        // Guatemala (T01 a T16)
        new Sucursal("TIENDA-01-CENTRAL", "MegaStore Central — Zona 10 (T01)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6010, -90.5130),
        new Sucursal("TIENDA-02-MIRAFLORES", "MegaStore Miraflores — Zona 11 (T02)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6215, -90.5530),
        new Sucursal("TIENDA-03-PORTALES", "MegaStore Portales — Zona 17 (T03)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6440, -90.4790),
        new Sucursal("TIENDA-04-PRADERA", "MegaStore Pradera Concepción (T04)", TipoUbicacion.TIENDA, Guatemala, Guatemala, "Santa Catarina Pinula", 14.5640, -90.4570),
        new Sucursal("TIENDA-05-OAKLAND", "MegaStore Oakland Mall — Zona 10 (T05)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.5985, -90.5075),
        new Sucursal("TIENDA-06-NARANJO", "MegaStore Naranjo Mall — Mixco (T06)", TipoUbicacion.TIENDA, Guatemala, Guatemala, "Mixco", 14.6440, -90.5490),
        new Sucursal("TIENDA-07-CAYALA", "MegaStore Paseo Cayalá — Zona 16 (T07)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6110, -90.4870),
        new Sucursal("TIENDA-08-ROOSEVELT", "MegaStore Peri-Roosevelt — Zona 7 (T08)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6290, -90.5600),
        new Sucursal("TIENDA-09-FONTABELLA", "MegaStore Plaza Fontabella — Zona 10 (T09)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6040, -90.5150),
        new Sucursal("TIENDA-10-METRONORTE", "MegaStore Metronorte — Zona 17 (T10)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6510, -90.4860),
        new Sucursal("TIENDA-11-MAJADAS", "MegaStore Parque Las Majadas — Zona 11 (T11)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6120, -90.5480),
        new Sucursal("TIENDA-12-AMERICAS", "MegaStore Plaza Las Américas — Zona 13 (T12)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.5880, -90.5250),
        new Sucursal("TIENDA-13-SANCRISTOBAL", "MegaStore San Cristóbal — Mixco (T13)", TipoUbicacion.TIENDA, Guatemala, Guatemala, "Mixco", 14.6000, -90.5880),
        new Sucursal("TIENDA-14-VILLANUEVA", "MegaStore Metrocentro — Villa Nueva (T14)", TipoUbicacion.TIENDA, Guatemala, Guatemala, "Villa Nueva", 14.5270, -90.5880),
        new Sucursal("TIENDA-15-CARRETERA", "MegaStore Pradera Express — Km 14 (T15)", TipoUbicacion.TIENDA, Guatemala, Guatemala, "Santa Catarina Pinula", 14.5760, -90.4700),
        new Sucursal("TIENDA-16-CENTRALNORTE", "MegaStore Centra Norte — Zona 18 (T16)", TipoUbicacion.TIENDA, Guatemala, Guatemala, Guatemala, 14.6620, -90.4770),

        // El Salvador (T17 a T24). Id heredados: no reflejan la ciudad.
        new Sucursal("TIENDA-17-XELA", "MegaStore Multiplaza — San Salvador (T17)", TipoUbicacion.TIENDA, ElSalvador, "La Libertad", "Antiguo Cuscatlán", 13.6785, -89.2520),
        new Sucursal("TIENDA-18-INTERPLAZA", "MegaStore Metrocentro — San Salvador (T18)", TipoUbicacion.TIENDA, ElSalvador, "San Salvador", "San Salvador", 13.7055, -89.2130),
        new Sucursal("TIENDA-19-ANTIGUA", "MegaStore Galerías Escalón — San Salvador (T19)", TipoUbicacion.TIENDA, ElSalvador, "San Salvador", "San Salvador", 13.7015, -89.2395),
        new Sucursal("TIENDA-20-ESCUINTLA", "MegaStore La Gran Vía — Santa Tecla (T20)", TipoUbicacion.TIENDA, ElSalvador, "La Libertad", "Santa Tecla", 13.6765, -89.2585),
        new Sucursal("TIENDA-21-MAZATENANGO", "MegaStore Plaza Mundo — Soyapango (T21)", TipoUbicacion.TIENDA, ElSalvador, "San Salvador", "Soyapango", 13.7020, -89.1530),
        new Sucursal("TIENDA-22-COBAN", "MegaStore Metrocentro — Santa Ana (T22)", TipoUbicacion.TIENDA, ElSalvador, "Santa Ana", "Santa Ana", 13.9830, -89.5500),
        new Sucursal("TIENDA-23-HUEHUETENANGO", "MegaStore Metrocentro — San Miguel (T23)", TipoUbicacion.TIENDA, ElSalvador, "San Miguel", "San Miguel", 13.4800, -88.1700),
        new Sucursal("TIENDA-24-CHIMALTENANGO", "MegaStore Plaza El Encuentro — Sonsonate (T24)", TipoUbicacion.TIENDA, ElSalvador, "Sonsonate", "Sonsonate", 13.7180, -89.7270),

        // Costa Rica (T25 a T32). Departamento guarda la provincia y municipio el canton.
        // Id heredados: no reflejan la ciudad.
        new Sucursal("TIENDA-25-ZACAPA", "MegaStore Multiplaza Escazú — San José (T25)", TipoUbicacion.TIENDA, CostaRica, "San José", "Escazú", 9.9390, -84.1440),
        new Sucursal("TIENDA-26-CHIQUIMULA", "MegaStore Mall San Pedro — San José (T26)", TipoUbicacion.TIENDA, CostaRica, "San José", "Montes de Oca", 9.9330, -84.0560),
        new Sucursal("TIENDA-27-PUERTOBARRIOS", "MegaStore City Mall — Alajuela (T27)", TipoUbicacion.TIENDA, CostaRica, "Alajuela", "Alajuela", 10.0050, -84.2050),
        new Sucursal("TIENDA-28-PETEN", "MegaStore Multiplaza del Este — Curridabat (T28)", TipoUbicacion.TIENDA, CostaRica, "San José", "Curridabat", 9.9160, -84.0390),
        new Sucursal("TIENDA-29-RETALHULEU", "MegaStore Paseo de las Flores — Heredia (T29)", TipoUbicacion.TIENDA, CostaRica, "Heredia", "Heredia", 9.9850, -84.1190),
        new Sucursal("TIENDA-30-COATEPEQUE", "MegaStore Paseo Metrópoli — Cartago (T30)", TipoUbicacion.TIENDA, CostaRica, "Cartago", "Cartago", 9.8730, -83.9420),
        new Sucursal("TIENDA-31-JUTIAPA", "MegaStore Plaza Liberia — Guanacaste (T31)", TipoUbicacion.TIENDA, CostaRica, "Guanacaste", "Liberia", 10.6340, -85.4400),
        new Sucursal("TIENDA-32-SALAMA", "MegaStore Paseo del Mar — Puntarenas (T32)", TipoUbicacion.TIENDA, CostaRica, "Puntarenas", "Puntarenas", 9.9760, -84.8330)
    };
}
