using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Cobranza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "abono_correlativo",
                schema: "credito");

            migrationBuilder.AddColumn<DateTime>(
                name: "pagada_en",
                schema: "credito",
                table: "cuenta_por_cobrar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pago_puntual",
                schema: "credito",
                table: "cuenta_por_cobrar",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "recordatorio_enviado_en",
                schema: "credito",
                table: "cuenta_por_cobrar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "abono_credito",
                schema: "credito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    estado = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    intento_numero = table.Column<int>(type: "integer", nullable: false),
                    codigo_respuesta = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    autorizacion_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ultimos_cuatro = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    marca = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    solicitado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abono_credito", x => x.id);
                    table.CheckConstraint("ck_abono_credito_monto_positivo", "monto > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuenta_por_cobrar_estado_vence_en",
                schema: "credito",
                table: "cuenta_por_cobrar",
                columns: new[] { "estado", "vence_en" });

            migrationBuilder.CreateIndex(
                name: "ix_abono_credito_cliente_id",
                schema: "credito",
                table: "abono_credito",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_abono_credito_cuenta_estado",
                schema: "credito",
                table: "abono_credito",
                columns: new[] { "cuenta_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_abono_credito_cuenta_en_curso",
                schema: "credito",
                table: "abono_credito",
                column: "cuenta_id",
                unique: true,
                filter: "estado IN ('SOLICITADO', 'PENDIENTE')");

            migrationBuilder.CreateIndex(
                name: "ux_abono_credito_referencia",
                schema: "credito",
                table: "abono_credito",
                column: "referencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abono_credito",
                schema: "credito");

            migrationBuilder.DropIndex(
                name: "ix_cuenta_por_cobrar_estado_vence_en",
                schema: "credito",
                table: "cuenta_por_cobrar");

            migrationBuilder.DropColumn(
                name: "pagada_en",
                schema: "credito",
                table: "cuenta_por_cobrar");

            migrationBuilder.DropColumn(
                name: "pago_puntual",
                schema: "credito",
                table: "cuenta_por_cobrar");

            migrationBuilder.DropColumn(
                name: "recordatorio_enviado_en",
                schema: "credito",
                table: "cuenta_por_cobrar");

            migrationBuilder.DropSequence(
                name: "abono_correlativo",
                schema: "credito");
        }
    }
}
