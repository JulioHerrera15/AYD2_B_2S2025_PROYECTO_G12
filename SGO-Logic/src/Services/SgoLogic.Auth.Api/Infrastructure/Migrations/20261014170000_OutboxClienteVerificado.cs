using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Auth.Api.Infrastructure.Migrations;

[DbContext(typeof(AuthDbContext))]
[Migration("20261014170000_OutboxClienteVerificado")]
public sealed class OutboxClienteVerificado : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                payload = table.Column<string>(type: "text", nullable: false),
                transaction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                exchange = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                routing_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                aggregate_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                aggregate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                fecha_publicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                intentos_fallidos = table.Column<int>(type: "integer", nullable: false),
                proximo_intento_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_event_id",
            schema: "public",
            table: "outbox_messages",
            column: "event_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_estado_proximo_intento_en",
            schema: "public",
            table: "outbox_messages",
            columns: new[] { "estado", "proximo_intento_en" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "outbox_messages", schema: "public");
}