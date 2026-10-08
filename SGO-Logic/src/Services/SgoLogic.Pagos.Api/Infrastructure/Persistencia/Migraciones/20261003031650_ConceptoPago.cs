using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ConceptoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "concepto",
                schema: "pagos",
                table: "pago",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Los pagos existentes (y los seeds que no mencionan la columna) son cobros de orden.
                defaultValue: "ORDEN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concepto",
                schema: "pagos",
                table: "pago");
        }
    }
}
