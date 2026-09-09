using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CorrelativoOrdenYMedioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "orden_correlativo",
                schema: "ordenes");

            // defaultValue solo para poblar las filas existentes al aplicar la
            // migracion; el modelo no declara default, el medio de pago lo fija
            // siempre el servicio al crear la orden.
            migrationBuilder.AddColumn<string>(
                name: "medio_pago",
                schema: "ordenes",
                table: "orden",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "TARJETA");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "medio_pago",
                schema: "ordenes",
                table: "orden");

            migrationBuilder.DropSequence(
                name: "orden_correlativo",
                schema: "ordenes");
        }
    }
}
