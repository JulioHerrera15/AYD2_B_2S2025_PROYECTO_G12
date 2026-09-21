using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class TokenMedioPagoOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "token_medio_pago",
                schema: "ordenes",
                table: "orden",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_medio_pago",
                schema: "ordenes",
                table: "orden");
        }
    }
}
