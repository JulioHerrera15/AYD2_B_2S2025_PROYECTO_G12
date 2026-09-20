using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialDespacho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "despacho");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateSequence(
                name: "despacho_correlativo",
                schema: "despacho");

            migrationBuilder.CreateTable(
                name: "despacho",
                schema: "despacho",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    orden_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reserva_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    metodo_entrega = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ubicacion_origen_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tipo_ubicacion_origen = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ubicacion_recogida_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    direccion_entrega = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    guia_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tracking_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    transportista = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    detalle_estado = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    entregado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despacho", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "despacho",
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
                name: "linea_despacho",
                schema: "despacho",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despacho_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linea_despacho", x => x.id);
                    table.ForeignKey(
                        name: "FK_linea_despacho_despacho_despacho_id",
                        column: x => x.despacho_id,
                        principalSchema: "despacho",
                        principalTable: "despacho",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_despacho_orden_id",
                schema: "despacho",
                table: "despacho",
                column: "orden_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_despacho_ubicacion_estado",
                schema: "despacho",
                table: "despacho",
                columns: new[] { "ubicacion_origen_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_linea_despacho_despacho_id",
                schema: "despacho",
                table: "linea_despacho",
                column: "despacho_id");

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "despacho",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linea_despacho",
                schema: "despacho");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "despacho");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "despacho",
                schema: "despacho");

            migrationBuilder.DropSequence(
                name: "despacho_correlativo",
                schema: "despacho");
        }
    }
}
