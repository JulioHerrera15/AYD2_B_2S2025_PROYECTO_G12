using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.ConsultaEstado.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UbicacionOrigenOrdenesVista : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ubicacion_origen_id",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ubicacion_origen_id",
                schema: "consulta_estado",
                table: "ordenes_vista");
        }
    }
}
