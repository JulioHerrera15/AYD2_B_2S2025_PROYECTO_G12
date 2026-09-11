using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AlertaFiscalOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "alerta_fiscal",
                schema: "ordenes",
                table: "orden",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "alerta_fiscal_en",
                schema: "ordenes",
                table: "orden",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "alerta_fiscal_motivo",
                schema: "ordenes",
                table: "orden",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alerta_fiscal",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "alerta_fiscal_en",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "alerta_fiscal_motivo",
                schema: "ordenes",
                table: "orden");
        }
    }
}
