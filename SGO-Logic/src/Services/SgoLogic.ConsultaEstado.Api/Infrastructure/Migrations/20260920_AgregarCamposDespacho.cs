using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.ConsultaEstado.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCamposDespacho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "despacho_id",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guia_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "despacho_id",
                schema: "consulta_estado",
                table: "ordenes_vista");

            migrationBuilder.DropColumn(
                name: "guia_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista");

            migrationBuilder.DropColumn(
                name: "estado_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista");
        }
    }
}
