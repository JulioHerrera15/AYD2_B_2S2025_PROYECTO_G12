using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.IntegracionPos.Worker.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialIntegracionPos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mapeo_tienda_pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TiendaOrigenId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PosInstanciaId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mapeo_tienda_pos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
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
                name: "registro_envio_pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrdenId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NumeroOrden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TiendaId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PosInstanciaId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TipoEvento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EstadoEnvio = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodigoHttp = table.Column<int>(type: "integer", nullable: true),
                    Respuesta = table.Column<string>(type: "text", nullable: true),
                    IntentoNumero = table.Column<int>(type: "integer", nullable: false),
                    FechaEnvio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registro_envio_pos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mapeo_tienda_pos_TiendaOrigenId",
                table: "mapeo_tienda_pos",
                column: "TiendaOrigenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                table: "mensajes_procesados",
                columns: new[] { "event_id", "nombre_servicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_estado_proximo_intento_en",
                table: "outbox_messages",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registro_envio_pos_EventoId",
                table: "registro_envio_pos",
                column: "EventoId");

            migrationBuilder.CreateIndex(
                name: "IX_registro_envio_pos_OrdenId",
                table: "registro_envio_pos",
                column: "OrdenId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mapeo_tienda_pos");

            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "registro_envio_pos");
        }
    }
}
