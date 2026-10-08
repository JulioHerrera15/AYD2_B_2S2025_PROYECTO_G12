using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CancelacionOrdenNotaCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "cancelacion_orden_en",
                schema: "facturacion",
                table: "documento_fiscal",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "nota_credito_cancelacion_id",
                schema: "facturacion",
                table: "documento_fiscal",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancelacion_orden_en",
                schema: "facturacion",
                table: "documento_fiscal");

            migrationBuilder.DropColumn(
                name: "nota_credito_cancelacion_id",
                schema: "facturacion",
                table: "documento_fiscal");
        }
    }
}
