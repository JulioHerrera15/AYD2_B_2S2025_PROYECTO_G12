using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.IntegracionErp.Worker.Infrastructure.Migraciones;

[DbContext(typeof(IntegracionErpDbContext))]
[Migration("20261007010000_ClienteEnVentaErp")]
public sealed class ClienteEnVentaErp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClienteId",
            table: "ventas_erp",
            type: "character varying(254)",
            maxLength: 254,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClienteId",
            table: "ventas_erp");
    }
}