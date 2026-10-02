using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CarritoIdOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "carrito_id",
                schema: "ordenes",
                table: "orden",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carrito_id",
                schema: "ordenes",
                table: "orden");
        }
    }
}
