using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DireccionEntregaOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direccion_entrega_departamento",
                schema: "ordenes",
                table: "orden",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direccion_entrega_direccion",
                schema: "ordenes",
                table: "orden",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direccion_entrega_municipio",
                schema: "ordenes",
                table: "orden",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direccion_entrega_pais",
                schema: "ordenes",
                table: "orden",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direccion_entrega_referencia",
                schema: "ordenes",
                table: "orden",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "direccion_entrega_departamento",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "direccion_entrega_direccion",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "direccion_entrega_municipio",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "direccion_entrega_pais",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "direccion_entrega_referencia",
                schema: "ordenes",
                table: "orden");
        }
    }
}
