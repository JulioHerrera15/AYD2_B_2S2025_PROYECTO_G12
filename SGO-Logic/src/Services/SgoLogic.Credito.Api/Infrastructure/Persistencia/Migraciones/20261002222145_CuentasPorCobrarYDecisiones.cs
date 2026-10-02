using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CuentasPorCobrarYDecisiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cuenta_por_cobrar",
                schema: "credito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero_orden = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    monto_original = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    saldo_pendiente = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    emitida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    vence_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    anulada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_anulacion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cuenta_por_cobrar", x => x.id);
                    table.CheckConstraint("ck_cuenta_por_cobrar_monto_positivo", "monto_original > 0");
                    table.CheckConstraint("ck_cuenta_por_cobrar_saldo_no_negativo", "saldo_pendiente >= 0");
                });

            migrationBuilder.CreateTable(
                name: "decision_credito",
                schema: "credito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo_motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    puntaje = table.Column<int>(type: "integer", nullable: false),
                    nivel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    limite_aprobado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    disponible_antes = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    version_politica = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    decidido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_credito", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuenta_por_cobrar_cliente_id",
                schema: "credito",
                table: "cuenta_por_cobrar",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ux_cuenta_por_cobrar_orden_id",
                schema: "credito",
                table: "cuenta_por_cobrar",
                column: "orden_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_decision_credito_orden_id",
                schema: "credito",
                table: "decision_credito",
                column: "orden_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cuenta_por_cobrar",
                schema: "credito");

            migrationBuilder.DropTable(
                name: "decision_credito",
                schema: "credito");
        }
    }
}
