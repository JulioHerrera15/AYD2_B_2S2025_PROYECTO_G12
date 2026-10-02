using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ClienteDespacho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cliente_id",
                schema: "despacho",
                table: "despacho",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cliente_id",
                schema: "despacho",
                table: "despacho");
        }
    }
}
