using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Sucursales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sucursal",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_ubicacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    departamento = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    municipio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    latitud = table.Column<double>(type: "double precision", nullable: true),
                    longitud = table.Column<double>(type: "double precision", nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sucursal", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "inventario",
                table: "sucursal",
                columns: new[] { "id", "activa", "departamento", "latitud", "longitud", "municipio", "nombre", "tipo_ubicacion" },
                values: new object[,]
                {
                    { "CD-01", true, "Guatemala", 14.664999999999999, -90.471999999999994, "Guatemala", "Centro de Distribución Norte — Zona 18 (CD-01)", "CENTRO_DISTRIBUCION" },
                    { "CD-CENTRAL", true, "Guatemala", 14.586, -90.542000000000002, "Guatemala", "Centro de Distribución Central — Zona 12", "CENTRO_DISTRIBUCION" },
                    { "TIENDA-01-CENTRAL", true, "Guatemala", 14.601000000000001, -90.513000000000005, "Guatemala", "MegaStore Central — Zona 10 (T01)", "TIENDA" },
                    { "TIENDA-02-MIRAFLORES", true, "Guatemala", 14.621499999999999, -90.552999999999997, "Guatemala", "MegaStore Miraflores — Zona 11 (T02)", "TIENDA" },
                    { "TIENDA-03-PORTALES", true, "Guatemala", 14.644, -90.478999999999999, "Guatemala", "MegaStore Portales — Zona 17 (T03)", "TIENDA" },
                    { "TIENDA-04-PRADERA", true, "Guatemala", 14.564, -90.456999999999994, "Santa Catarina Pinula", "MegaStore Pradera Concepción (T04)", "TIENDA" },
                    { "TIENDA-05-OAKLAND", true, "Guatemala", 14.5985, -90.507499999999993, "Guatemala", "MegaStore Oakland Mall — Zona 10 (T05)", "TIENDA" },
                    { "TIENDA-06-NARANJO", true, "Guatemala", 14.644, -90.549000000000007, "Mixco", "MegaStore Naranjo Mall — Mixco (T06)", "TIENDA" },
                    { "TIENDA-07-CAYALA", true, "Guatemala", 14.611000000000001, -90.486999999999995, "Guatemala", "MegaStore Paseo Cayalá — Zona 16 (T07)", "TIENDA" },
                    { "TIENDA-08-ROOSEVELT", true, "Guatemala", 14.629, -90.560000000000002, "Guatemala", "MegaStore Peri-Roosevelt — Zona 7 (T08)", "TIENDA" },
                    { "TIENDA-09-FONTABELLA", true, "Guatemala", 14.603999999999999, -90.515000000000001, "Guatemala", "MegaStore Plaza Fontabella — Zona 10 (T09)", "TIENDA" },
                    { "TIENDA-10-METRONORTE", true, "Guatemala", 14.651, -90.486000000000004, "Guatemala", "MegaStore Metronorte — Zona 17 (T10)", "TIENDA" },
                    { "TIENDA-11-MAJADAS", true, "Guatemala", 14.612, -90.548000000000002, "Guatemala", "MegaStore Parque Las Majadas — Zona 11 (T11)", "TIENDA" },
                    { "TIENDA-12-AMERICAS", true, "Guatemala", 14.587999999999999, -90.525000000000006, "Guatemala", "MegaStore Plaza Las Américas — Zona 13 (T12)", "TIENDA" },
                    { "TIENDA-13-SANCRISTOBAL", true, "Guatemala", 14.6, -90.587999999999994, "Mixco", "MegaStore San Cristóbal — Mixco (T13)", "TIENDA" },
                    { "TIENDA-14-VILLANUEVA", true, "Guatemala", 14.526999999999999, -90.587999999999994, "Villa Nueva", "MegaStore Metrocentro — Villa Nueva (T14)", "TIENDA" },
                    { "TIENDA-15-CARRETERA", true, "Guatemala", 14.576000000000001, -90.469999999999999, "Santa Catarina Pinula", "MegaStore Pradera Express — Km 14 (T15)", "TIENDA" },
                    { "TIENDA-16-CENTRALNORTE", true, "Guatemala", 14.662000000000001, -90.477000000000004, "Guatemala", "MegaStore Centra Norte — Zona 18 (T16)", "TIENDA" },
                    { "TIENDA-17-XELA", true, "Quetzaltenango", 14.846, -91.530000000000001, "Quetzaltenango", "MegaStore Utz Ulew — Quetzaltenango (T17)", "TIENDA" },
                    { "TIENDA-18-INTERPLAZA", true, "Quetzaltenango", 14.855, -91.518000000000001, "Quetzaltenango", "MegaStore Interplaza Xela — Quetzaltenango (T18)", "TIENDA" },
                    { "TIENDA-19-ANTIGUA", true, "Sacatepéquez", 14.558, -90.733000000000004, "Antigua Guatemala", "MegaStore La Antigua — Sacatepéquez (T19)", "TIENDA" },
                    { "TIENDA-20-ESCUINTLA", true, "Escuintla", 14.305, -90.784999999999997, "Escuintla", "MegaStore Escuintla Centro (T20)", "TIENDA" },
                    { "TIENDA-21-MAZATENANGO", true, "Suchitepéquez", 14.534000000000001, -91.503, "Mazatenango", "MegaStore Mazatenango — Suchitepéquez (T21)", "TIENDA" },
                    { "TIENDA-22-COBAN", true, "Alta Verapaz", 15.471, -90.370999999999995, "Cobán", "MegaStore Cobán — Alta Verapaz (T22)", "TIENDA" },
                    { "TIENDA-23-HUEHUETENANGO", true, "Huehuetenango", 15.319000000000001, -91.471000000000004, "Huehuetenango", "MegaStore Huehuetenango Centro (T23)", "TIENDA" },
                    { "TIENDA-24-CHIMALTENANGO", true, "Chimaltenango", 14.661, -90.819999999999993, "Chimaltenango", "MegaStore Chimaltenango Centro (T24)", "TIENDA" },
                    { "TIENDA-25-ZACAPA", true, "Zacapa", 14.972, -89.530000000000001, "Zacapa", "MegaStore Zacapa Centro (T25)", "TIENDA" },
                    { "TIENDA-26-CHIQUIMULA", true, "Chiquimula", 14.800000000000001, -89.545000000000002, "Chiquimula", "MegaStore Chiquimula Centro (T26)", "TIENDA" },
                    { "TIENDA-27-PUERTOBARRIOS", true, "Izabal", 15.727, -88.593999999999994, "Puerto Barrios", "MegaStore Puerto Barrios — Izabal (T27)", "TIENDA" },
                    { "TIENDA-28-PETEN", true, "Petén", 16.920000000000002, -89.891999999999996, "Flores", "MegaStore Santa Elena — Petén (T28)", "TIENDA" },
                    { "TIENDA-29-RETALHULEU", true, "Retalhuleu", 14.537000000000001, -91.677000000000007, "Retalhuleu", "MegaStore Retalhuleu Centro (T29)", "TIENDA" },
                    { "TIENDA-30-COATEPEQUE", true, "Quetzaltenango", 14.702999999999999, -91.864000000000004, "Coatepeque", "MegaStore Coatepeque — Quetzaltenango (T30)", "TIENDA" },
                    { "TIENDA-31-JUTIAPA", true, "Jutiapa", 14.292, -89.894999999999996, "Jutiapa", "MegaStore Jutiapa Centro (T31)", "TIENDA" },
                    { "TIENDA-32-SALAMA", true, "Baja Verapaz", 15.103, -90.316000000000003, "Salamá", "MegaStore Salamá — Baja Verapaz (T32)", "TIENDA" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sucursal",
                schema: "inventario");
        }
    }
}
