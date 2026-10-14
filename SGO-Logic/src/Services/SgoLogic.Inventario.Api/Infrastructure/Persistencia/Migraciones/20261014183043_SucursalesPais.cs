using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class SucursalesPais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pais",
                schema: "inventario",
                table: "sucursal",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "CD-01",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "CD-CENTRAL",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-01-CENTRAL",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-02-MIRAFLORES",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-03-PORTALES",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-04-PRADERA",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-05-OAKLAND",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-06-NARANJO",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-07-CAYALA",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-08-ROOSEVELT",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-09-FONTABELLA",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-10-METRONORTE",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-11-MAJADAS",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-12-AMERICAS",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-13-SANCRISTOBAL",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-14-VILLANUEVA",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-15-CARRETERA",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-16-CENTRALNORTE",
                column: "pais",
                value: "Guatemala");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-17-XELA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "La Libertad", 13.6785, -89.251999999999995, "Antiguo Cuscatlán", "MegaStore Multiplaza — San Salvador (T17)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-18-INTERPLAZA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San Salvador", 13.705500000000001, -89.212999999999994, "San Salvador", "MegaStore Metrocentro — San Salvador (T18)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-19-ANTIGUA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San Salvador", 13.701499999999999, -89.239500000000007, "San Salvador", "MegaStore Galerías Escalón — San Salvador (T19)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-20-ESCUINTLA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "La Libertad", 13.676500000000001, -89.258499999999998, "Santa Tecla", "MegaStore La Gran Vía — Santa Tecla (T20)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-21-MAZATENANGO",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San Salvador", 13.702, -89.153000000000006, "Soyapango", "MegaStore Plaza Mundo — Soyapango (T21)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-22-COBAN",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Santa Ana", 13.983000000000001, -89.549999999999997, "Santa Ana", "MegaStore Metrocentro — Santa Ana (T22)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-23-HUEHUETENANGO",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San Miguel", 13.48, -88.170000000000002, "San Miguel", "MegaStore Metrocentro — San Miguel (T23)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-24-CHIMALTENANGO",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Sonsonate", 13.718, -89.727000000000004, "Sonsonate", "MegaStore Plaza El Encuentro — Sonsonate (T24)", "El Salvador" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-25-ZACAPA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San José", 9.9390000000000001, -84.144000000000005, "Escazú", "MegaStore Multiplaza Escazú — San José (T25)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-26-CHIQUIMULA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San José", 9.9329999999999998, -84.055999999999997, "Montes de Oca", "MegaStore Mall San Pedro — San José (T26)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-27-PUERTOBARRIOS",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Alajuela", 10.005000000000001, -84.204999999999998, "Alajuela", "MegaStore City Mall — Alajuela (T27)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-28-PETEN",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "San José", 9.9160000000000004, -84.039000000000001, "Curridabat", "MegaStore Multiplaza del Este — Curridabat (T28)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-29-RETALHULEU",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Heredia", 9.9849999999999994, -84.119, "Heredia", "MegaStore Paseo de las Flores — Heredia (T29)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-30-COATEPEQUE",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Cartago", 9.8729999999999993, -83.941999999999993, "Cartago", "MegaStore Paseo Metrópoli — Cartago (T30)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-31-JUTIAPA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Guanacaste", 10.634, -85.439999999999998, "Liberia", "MegaStore Plaza Liberia — Guanacaste (T31)", "Costa Rica" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-32-SALAMA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre", "pais" },
                values: new object[] { "Puntarenas", 9.9760000000000009, -84.832999999999998, "Puntarenas", "MegaStore Paseo del Mar — Puntarenas (T32)", "Costa Rica" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pais",
                schema: "inventario",
                table: "sucursal");

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-17-XELA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Quetzaltenango", 14.846, -91.530000000000001, "Quetzaltenango", "MegaStore Utz Ulew — Quetzaltenango (T17)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-18-INTERPLAZA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Quetzaltenango", 14.855, -91.518000000000001, "Quetzaltenango", "MegaStore Interplaza Xela — Quetzaltenango (T18)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-19-ANTIGUA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Sacatepéquez", 14.558, -90.733000000000004, "Antigua Guatemala", "MegaStore La Antigua — Sacatepéquez (T19)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-20-ESCUINTLA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Escuintla", 14.305, -90.784999999999997, "Escuintla", "MegaStore Escuintla Centro (T20)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-21-MAZATENANGO",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Suchitepéquez", 14.534000000000001, -91.503, "Mazatenango", "MegaStore Mazatenango — Suchitepéquez (T21)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-22-COBAN",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Alta Verapaz", 15.471, -90.370999999999995, "Cobán", "MegaStore Cobán — Alta Verapaz (T22)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-23-HUEHUETENANGO",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Huehuetenango", 15.319000000000001, -91.471000000000004, "Huehuetenango", "MegaStore Huehuetenango Centro (T23)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-24-CHIMALTENANGO",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Chimaltenango", 14.661, -90.819999999999993, "Chimaltenango", "MegaStore Chimaltenango Centro (T24)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-25-ZACAPA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Zacapa", 14.972, -89.530000000000001, "Zacapa", "MegaStore Zacapa Centro (T25)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-26-CHIQUIMULA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Chiquimula", 14.800000000000001, -89.545000000000002, "Chiquimula", "MegaStore Chiquimula Centro (T26)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-27-PUERTOBARRIOS",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Izabal", 15.727, -88.593999999999994, "Puerto Barrios", "MegaStore Puerto Barrios — Izabal (T27)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-28-PETEN",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Petén", 16.920000000000002, -89.891999999999996, "Flores", "MegaStore Santa Elena — Petén (T28)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-29-RETALHULEU",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Retalhuleu", 14.537000000000001, -91.677000000000007, "Retalhuleu", "MegaStore Retalhuleu Centro (T29)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-30-COATEPEQUE",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Quetzaltenango", 14.702999999999999, -91.864000000000004, "Coatepeque", "MegaStore Coatepeque — Quetzaltenango (T30)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-31-JUTIAPA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Jutiapa", 14.292, -89.894999999999996, "Jutiapa", "MegaStore Jutiapa Centro (T31)" });

            migrationBuilder.UpdateData(
                schema: "inventario",
                table: "sucursal",
                keyColumn: "id",
                keyValue: "TIENDA-32-SALAMA",
                columns: new[] { "departamento", "latitud", "longitud", "municipio", "nombre" },
                values: new object[] { "Baja Verapaz", 15.103, -90.316000000000003, "Salamá", "MegaStore Salamá — Baja Verapaz (T32)" });
        }
    }
}
