using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Catalogo de las 34 ubicaciones que existen en los seeds (scripts/seed/sql y
/// scripts/demo): 2 centros de distribucion y 32 tiendas. Lo carga la migracion
/// Sucursales via HasData, asi que el catalogo existe aunque el seed de datos este
/// apagado.
///
/// Los identificadores son exactamente los de <c>existencia.ubicacion_id</c>. Las
/// coordenadas son aproximadas pero reales (centro comercial o casco urbano del
/// municipio), suficientes para ordenar por cercania.
/// </summary>
internal static class SucursalesSemilla
{
    private const string Guatemala = "Guatemala";

    public static IReadOnlyList<Sucursal> Todas { get; } = new[]
    {
        new Sucursal("CD-CENTRAL", "Centro de Distribución Central — Zona 12", TipoUbicacion.CENTRO_DISTRIBUCION, Guatemala, Guatemala, 14.5860, -90.5420),
        new Sucursal("CD-01", "Centro de Distribución Norte — Zona 18 (CD-01)", TipoUbicacion.CENTRO_DISTRIBUCION, Guatemala, Guatemala, 14.6650, -90.4720),

        new Sucursal("TIENDA-01-CENTRAL", "MegaStore Central — Zona 10 (T01)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6010, -90.5130),
        new Sucursal("TIENDA-02-MIRAFLORES", "MegaStore Miraflores — Zona 11 (T02)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6215, -90.5530),
        new Sucursal("TIENDA-03-PORTALES", "MegaStore Portales — Zona 17 (T03)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6440, -90.4790),
        new Sucursal("TIENDA-04-PRADERA", "MegaStore Pradera Concepción (T04)", TipoUbicacion.TIENDA, Guatemala, "Santa Catarina Pinula", 14.5640, -90.4570),
        new Sucursal("TIENDA-05-OAKLAND", "MegaStore Oakland Mall — Zona 10 (T05)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.5985, -90.5075),
        new Sucursal("TIENDA-06-NARANJO", "MegaStore Naranjo Mall — Mixco (T06)", TipoUbicacion.TIENDA, Guatemala, "Mixco", 14.6440, -90.5490),
        new Sucursal("TIENDA-07-CAYALA", "MegaStore Paseo Cayalá — Zona 16 (T07)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6110, -90.4870),
        new Sucursal("TIENDA-08-ROOSEVELT", "MegaStore Peri-Roosevelt — Zona 7 (T08)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6290, -90.5600),
        new Sucursal("TIENDA-09-FONTABELLA", "MegaStore Plaza Fontabella — Zona 10 (T09)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6040, -90.5150),
        new Sucursal("TIENDA-10-METRONORTE", "MegaStore Metronorte — Zona 17 (T10)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6510, -90.4860),
        new Sucursal("TIENDA-11-MAJADAS", "MegaStore Parque Las Majadas — Zona 11 (T11)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6120, -90.5480),
        new Sucursal("TIENDA-12-AMERICAS", "MegaStore Plaza Las Américas — Zona 13 (T12)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.5880, -90.5250),
        new Sucursal("TIENDA-13-SANCRISTOBAL", "MegaStore San Cristóbal — Mixco (T13)", TipoUbicacion.TIENDA, Guatemala, "Mixco", 14.6000, -90.5880),
        new Sucursal("TIENDA-14-VILLANUEVA", "MegaStore Metrocentro — Villa Nueva (T14)", TipoUbicacion.TIENDA, Guatemala, "Villa Nueva", 14.5270, -90.5880),
        new Sucursal("TIENDA-15-CARRETERA", "MegaStore Pradera Express — Km 14 (T15)", TipoUbicacion.TIENDA, Guatemala, "Santa Catarina Pinula", 14.5760, -90.4700),
        new Sucursal("TIENDA-16-CENTRALNORTE", "MegaStore Centra Norte — Zona 18 (T16)", TipoUbicacion.TIENDA, Guatemala, Guatemala, 14.6620, -90.4770),
        new Sucursal("TIENDA-17-XELA", "MegaStore Utz Ulew — Quetzaltenango (T17)", TipoUbicacion.TIENDA, "Quetzaltenango", "Quetzaltenango", 14.8460, -91.5300),
        new Sucursal("TIENDA-18-INTERPLAZA", "MegaStore Interplaza Xela — Quetzaltenango (T18)", TipoUbicacion.TIENDA, "Quetzaltenango", "Quetzaltenango", 14.8550, -91.5180),
        new Sucursal("TIENDA-19-ANTIGUA", "MegaStore La Antigua — Sacatepéquez (T19)", TipoUbicacion.TIENDA, "Sacatepéquez", "Antigua Guatemala", 14.5580, -90.7330),
        new Sucursal("TIENDA-20-ESCUINTLA", "MegaStore Escuintla Centro (T20)", TipoUbicacion.TIENDA, "Escuintla", "Escuintla", 14.3050, -90.7850),
        new Sucursal("TIENDA-21-MAZATENANGO", "MegaStore Mazatenango — Suchitepéquez (T21)", TipoUbicacion.TIENDA, "Suchitepéquez", "Mazatenango", 14.5340, -91.5030),
        new Sucursal("TIENDA-22-COBAN", "MegaStore Cobán — Alta Verapaz (T22)", TipoUbicacion.TIENDA, "Alta Verapaz", "Cobán", 15.4710, -90.3710),
        new Sucursal("TIENDA-23-HUEHUETENANGO", "MegaStore Huehuetenango Centro (T23)", TipoUbicacion.TIENDA, "Huehuetenango", "Huehuetenango", 15.3190, -91.4710),
        new Sucursal("TIENDA-24-CHIMALTENANGO", "MegaStore Chimaltenango Centro (T24)", TipoUbicacion.TIENDA, "Chimaltenango", "Chimaltenango", 14.6610, -90.8200),
        new Sucursal("TIENDA-25-ZACAPA", "MegaStore Zacapa Centro (T25)", TipoUbicacion.TIENDA, "Zacapa", "Zacapa", 14.9720, -89.5300),
        new Sucursal("TIENDA-26-CHIQUIMULA", "MegaStore Chiquimula Centro (T26)", TipoUbicacion.TIENDA, "Chiquimula", "Chiquimula", 14.8000, -89.5450),
        new Sucursal("TIENDA-27-PUERTOBARRIOS", "MegaStore Puerto Barrios — Izabal (T27)", TipoUbicacion.TIENDA, "Izabal", "Puerto Barrios", 15.7270, -88.5940),
        new Sucursal("TIENDA-28-PETEN", "MegaStore Santa Elena — Petén (T28)", TipoUbicacion.TIENDA, "Petén", "Flores", 16.9200, -89.8920),
        new Sucursal("TIENDA-29-RETALHULEU", "MegaStore Retalhuleu Centro (T29)", TipoUbicacion.TIENDA, "Retalhuleu", "Retalhuleu", 14.5370, -91.6770),
        new Sucursal("TIENDA-30-COATEPEQUE", "MegaStore Coatepeque — Quetzaltenango (T30)", TipoUbicacion.TIENDA, "Quetzaltenango", "Coatepeque", 14.7030, -91.8640),
        new Sucursal("TIENDA-31-JUTIAPA", "MegaStore Jutiapa Centro (T31)", TipoUbicacion.TIENDA, "Jutiapa", "Jutiapa", 14.2920, -89.8950),
        new Sucursal("TIENDA-32-SALAMA", "MegaStore Salamá — Baja Verapaz (T32)", TipoUbicacion.TIENDA, "Baja Verapaz", "Salamá", 15.1030, -90.3160)
    };
}
