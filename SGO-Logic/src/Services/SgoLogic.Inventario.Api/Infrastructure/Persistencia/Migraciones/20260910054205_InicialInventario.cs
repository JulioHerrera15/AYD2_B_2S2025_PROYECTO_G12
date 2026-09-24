using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventario");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateSequence(
                name: "reserva_correlativo",
                schema: "inventario");

            migrationBuilder.CreateTable(
                name: "existencia",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ubicacion_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo_ubicacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cantidad_fisica = table.Column<int>(type: "integer", nullable: false),
                    cantidad_reservada = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_existencia", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nombre_servicio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fecha_proceso = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensajes_procesados", x => x.id);
                });

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
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reserva",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ubicacion_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo_ubicacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ttl_segundos = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reserva", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "linea_reserva",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    reserva_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linea_reserva", x => x.id);
                    table.ForeignKey(
                        name: "fk_linea_reserva_reserva",
                        column: x => x.reserva_id,
                        principalSchema: "inventario",
                        principalTable: "reserva",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_existencia_sku_ubicacion",
                schema: "inventario",
                table: "existencia",
                columns: new[] { "sku", "ubicacion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linea_reserva_reserva_id",
                schema: "inventario",
                table: "linea_reserva",
                column: "reserva_id");

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "inventario",
                table: "mensajes_procesados",
                columns: new[] { "event_id", "nombre_servicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_estado_proximo_intento_en",
                schema: "public",
                table: "outbox_messages",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "public",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reserva_estado_expira_en",
                schema: "inventario",
                table: "reserva",
                columns: new[] { "estado", "expira_en" });

            migrationBuilder.CreateIndex(
                name: "ix_reserva_orden_id",
                schema: "inventario",
                table: "reserva",
                column: "orden_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "existencia",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "linea_reserva",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "reserva",
                schema: "inventario");

            migrationBuilder.DropSequence(
                name: "reserva_correlativo",
                schema: "inventario");
        }
    }
}
