using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialPagos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pagos");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "enlace_reintento",
                schema: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usado = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enlace_reintento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "pagos",
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
                name: "pago",
                schema: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    medio_pago = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pago", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "intento_pago",
                schema: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intento_numero = table.Column<int>(type: "integer", nullable: false),
                    medio_pago = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token_medio_pago = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ultimos_cuatro = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    marca = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo_respuesta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    mensaje_respuesta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    latencia_ms = table.Column<int>(type: "integer", nullable: true),
                    autorizacion_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    iniciado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finalizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intento_pago", x => x.id);
                    table.ForeignKey(
                        name: "fk_intento_pago_pago",
                        column: x => x.pago_id,
                        principalSchema: "pagos",
                        principalTable: "pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enlace_reintento_orden_id",
                schema: "pagos",
                table: "enlace_reintento",
                column: "orden_id");

            migrationBuilder.CreateIndex(
                name: "ix_enlace_reintento_token",
                schema: "pagos",
                table: "enlace_reintento",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_intento_pago_pago_id_intento_numero",
                schema: "pagos",
                table: "intento_pago",
                columns: new[] { "pago_id", "intento_numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "pagos",
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
                name: "ix_pago_orden_id",
                schema: "pagos",
                table: "pago",
                column: "orden_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enlace_reintento",
                schema: "pagos");

            migrationBuilder.DropTable(
                name: "intento_pago",
                schema: "pagos");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "pagos");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pago",
                schema: "pagos");
        }
    }
}
