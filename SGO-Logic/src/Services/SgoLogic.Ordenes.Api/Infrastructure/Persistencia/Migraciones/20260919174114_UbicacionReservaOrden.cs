using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class UbicacionReservaOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ubicacion_reserva_id",
                schema: "ordenes",
                table: "orden",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ubicacion_reserva_id",
                schema: "ordenes",
                table: "orden");
        }
    }
}
