using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DevolucionesYReembolsos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "monto_reembolsado",
                schema: "credito",
                table: "abono_credito",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "monto_reembolso_solicitado",
                schema: "credito",
                table: "abono_credito",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "motivo_falla_reembolso",
                schema: "credito",
                table: "abono_credito",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reembolso_fallido_en",
                schema: "credito",
                table: "abono_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reembolso_solicitado_en",
                schema: "credito",
                table: "abono_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "movimiento_cuenta",
                schema: "credito",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    referencia = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    monto_descontado_deuda = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    monto_a_reembolsar = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    abono_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimiento_cuenta", x => x.id);
                    table.CheckConstraint("ck_movimiento_cuenta_monto_positivo", "monto > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_abono_credito_reembolso_sin_solicitud",
                schema: "credito",
                table: "abono_credito",
                column: "estado",
                filter: "reembolso_solicitado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_cuenta_cliente_id",
                schema: "credito",
                table: "movimiento_cuenta",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_cuenta_cuenta_id",
                schema: "credito",
                table: "movimiento_cuenta",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ux_movimiento_cuenta_tipo_referencia",
                schema: "credito",
                table: "movimiento_cuenta",
                columns: new[] { "tipo", "referencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimiento_cuenta",
                schema: "credito");

            migrationBuilder.DropIndex(
                name: "ix_abono_credito_reembolso_sin_solicitud",
                schema: "credito",
                table: "abono_credito");

            migrationBuilder.DropColumn(
                name: "monto_reembolsado",
                schema: "credito",
                table: "abono_credito");

            migrationBuilder.DropColumn(
                name: "monto_reembolso_solicitado",
                schema: "credito",
                table: "abono_credito");

            migrationBuilder.DropColumn(
                name: "motivo_falla_reembolso",
                schema: "credito",
                table: "abono_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_fallido_en",
                schema: "credito",
                table: "abono_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_solicitado_en",
                schema: "credito",
                table: "abono_credito");
        }
    }
}
