using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.ConsultaEstado.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCamposDespachoOrdenesVista : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ultimo_sequence",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "subtotal",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "impuestos",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "despacho_id",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guia_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "character varying(80)",
                maxLength: 80,
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
                name: "estado_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista");

            migrationBuilder.DropColumn(
                name: "guia_despacho",
                schema: "consulta_estado",
                table: "ordenes_vista");

            migrationBuilder.AlterColumn<long>(
                name: "ultimo_sequence",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "subtotal",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "impuestos",
                schema: "consulta_estado",
                table: "ordenes_vista",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
