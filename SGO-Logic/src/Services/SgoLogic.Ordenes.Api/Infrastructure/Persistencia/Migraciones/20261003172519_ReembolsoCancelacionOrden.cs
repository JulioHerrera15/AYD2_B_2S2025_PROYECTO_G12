using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ReembolsoCancelacionOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "estado_reembolso",
                schema: "ordenes",
                table: "orden",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NO_APLICA");

            migrationBuilder.AddColumn<DateTime>(
                name: "reembolso_actualizado_en",
                schema: "ordenes",
                table: "orden",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "estado_reembolso",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropColumn(
                name: "reembolso_actualizado_en",
                schema: "ordenes",
                table: "orden");
        }
    }
}
