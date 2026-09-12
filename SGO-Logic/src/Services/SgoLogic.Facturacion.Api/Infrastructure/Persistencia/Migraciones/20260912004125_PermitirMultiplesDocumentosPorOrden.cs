using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class PermitirMultiplesDocumentosPorOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documento_fiscal_orden_id",
                schema: "facturacion",
                table: "documento_fiscal");

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_orden_id",
                schema: "facturacion",
                table: "documento_fiscal",
                column: "orden_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documento_fiscal_orden_id",
                schema: "facturacion",
                table: "documento_fiscal");

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_orden_id",
                schema: "facturacion",
                table: "documento_fiscal",
                column: "orden_id",
                unique: true);
        }
    }
}
