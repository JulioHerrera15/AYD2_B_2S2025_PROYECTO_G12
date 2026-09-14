using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class MovimientoExistencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movimiento_existencia",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ubicacion_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    cantidad_fisica_resultante = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    registrado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimiento_existencia", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_sku_ubicacion_registrado_en",
                schema: "inventario",
                table: "movimiento_existencia",
                columns: new[] { "sku", "ubicacion_id", "registrado_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimiento_existencia",
                schema: "inventario");
        }
    }
}
