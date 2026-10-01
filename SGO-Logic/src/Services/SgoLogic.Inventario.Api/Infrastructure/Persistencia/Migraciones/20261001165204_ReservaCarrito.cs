using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ReservaCarrito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reserva_carrito",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrito_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ubicacion_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo_ubicacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ubicacion_preferida_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reserva_carrito", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reserva_carrito_activa",
                schema: "inventario",
                table: "reserva_carrito",
                columns: new[] { "carrito_id", "sku" },
                unique: true,
                filter: "estado = 'ACTIVA'");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_carrito_estado_expira_en",
                schema: "inventario",
                table: "reserva_carrito",
                columns: new[] { "estado", "expira_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reserva_carrito",
                schema: "inventario");
        }
    }
}
