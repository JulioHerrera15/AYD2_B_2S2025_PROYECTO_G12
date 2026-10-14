using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.IntegracionErp.Worker.Infrastructure.Migraciones;

[DbContext(typeof(IntegracionErpDbContext))]
[Migration("20261014090000_PagosCreditoErp")]
public sealed class PagosCreditoErp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pagos_credito_erp",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                cuenta_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                orden_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                pagada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                puntual = table.Column<bool>(type: "boolean", nullable: false),
                estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                recibido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                enviado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_pagos_credito_erp", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_pagos_credito_erp_event_id",
            table: "pagos_credito_erp",
            column: "event_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_pagos_credito_erp_estado_recibido_en",
            table: "pagos_credito_erp",
            columns: new[] { "estado", "recibido_en" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "pagos_credito_erp");
}