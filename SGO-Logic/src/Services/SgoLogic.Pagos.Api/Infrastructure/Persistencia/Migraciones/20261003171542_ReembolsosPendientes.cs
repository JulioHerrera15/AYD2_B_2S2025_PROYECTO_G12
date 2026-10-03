using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ReembolsosPendientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reembolso",
                schema: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    referencia_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    abono_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    autorizacion_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    codigo_respuesta = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    motivo_falla = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reembolso", x => x.id);
                    table.CheckConstraint("ck_reembolso_monto_positivo", "monto > 0");
                    table.ForeignKey(
                        name: "fk_reembolso_pago",
                        column: x => x.pago_id,
                        principalSchema: "pagos",
                        principalTable: "pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reembolso_estado_proximo_intento",
                schema: "pagos",
                table: "reembolso",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_reembolso_pago_id",
                schema: "pagos",
                table: "reembolso",
                column: "pago_id");

            migrationBuilder.CreateIndex(
                name: "ux_reembolso_concepto_referencia",
                schema: "pagos",
                table: "reembolso",
                columns: new[] { "concepto", "referencia_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reembolso",
                schema: "pagos");
        }
    }
}
