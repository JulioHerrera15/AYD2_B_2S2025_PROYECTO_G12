using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CertificacionFiscalOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receptor_cui",
                schema: "ordenes",
                table: "orden",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "CF");

            migrationBuilder.AddColumn<string>(
                name: "pago_id",
                schema: "ordenes",
                table: "orden",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "documento_fiscal_id",
                schema: "ordenes",
                table: "orden",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "documento_fiscal_serie",
                schema: "ordenes",
                table: "orden",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "documento_fiscal_numero",
                schema: "ordenes",
                table: "orden",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "documento_fiscal_numero_autorizacion",
                schema: "ordenes",
                table: "orden",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "documento_fiscal_folio",
                schema: "ordenes",
                table: "orden",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "documento_fiscal_certificado_en",
                schema: "ordenes",
                table: "orden",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receptor_cui",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "pago_id",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "documento_fiscal_id",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "documento_fiscal_serie",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "documento_fiscal_numero",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "documento_fiscal_numero_autorizacion",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "documento_fiscal_folio",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "documento_fiscal_certificado_en",
                schema: "ordenes",
                table: "orden");
        }
    }
}
