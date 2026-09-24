using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Devoluciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "devolucion_correlativo",
                schema: "ordenes");

            migrationBuilder.CreateTable(
                name: "devolucion",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    orden_original_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    es_parcial = table.Column<bool>(type: "boolean", nullable: false),
                    ubicacion_reingreso_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    monto_reembolso = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    pago_original_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    documento_fiscal_original_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    registrada_por = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    registrada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devolucion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "linea_devolucion",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sku_reemplazo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    devolucion_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linea_devolucion", x => x.id);
                    table.ForeignKey(
                        name: "fk_linea_devolucion_devolucion",
                        column: x => x.devolucion_id,
                        principalSchema: "ordenes",
                        principalTable: "devolucion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devolucion_orden_original_id",
                schema: "ordenes",
                table: "devolucion",
                column: "orden_original_id");

            migrationBuilder.CreateIndex(
                name: "ix_linea_devolucion_devolucion_id",
                schema: "ordenes",
                table: "linea_devolucion",
                column: "devolucion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linea_devolucion",
                schema: "ordenes");

            migrationBuilder.DropTable(
                name: "devolucion",
                schema: "ordenes");

            migrationBuilder.DropSequence(
                name: "devolucion_correlativo",
                schema: "ordenes");
        }
    }
}
