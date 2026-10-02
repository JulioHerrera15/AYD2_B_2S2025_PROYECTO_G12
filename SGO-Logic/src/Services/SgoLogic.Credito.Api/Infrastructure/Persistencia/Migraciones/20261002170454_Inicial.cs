using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "credito");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "historial_cliente",
                schema: "credito",
                columns: table => new
                {
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    codigo_cliente_erp = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cliente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    compras_ultimos_12_meses = table.Column<int>(type: "integer", nullable: false),
                    monto_comprado_ultimos_12_meses = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pagos_puntuales = table.Column<int>(type: "integer", nullable: false),
                    pagos_tardios = table.Column<int>(type: "integer", nullable: false),
                    max_dias_atraso = table.Column<int>(type: "integer", nullable: false),
                    devoluciones = table.Column<int>(type: "integer", nullable: false),
                    incidentes_graves = table.Column<int>(type: "integer", nullable: false),
                    otros_incidentes = table.Column<int>(type: "integer", nullable: false),
                    fecha_corte = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lote_origen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recibido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historial_cliente", x => x.cliente_id);
                });

            migrationBuilder.CreateTable(
                name: "linea_credito",
                schema: "credito",
                columns: table => new
                {
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    limite_aprobado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    saldo_utilizado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linea_credito", x => x.cliente_id);
                    table.CheckConstraint("ck_linea_credito_limite_no_negativo", "limite_aprobado >= 0");
                    table.CheckConstraint("ck_linea_credito_saldo_no_negativo", "saldo_utilizado >= 0");
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "credito",
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
                name: "perfil_crediticio",
                schema: "credito",
                columns: table => new
                {
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    puntaje = table.Column<int>(type: "integer", nullable: false),
                    nivel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    elegible = table.Column<bool>(type: "boolean", nullable: false),
                    codigo_motivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    limite_aprobado = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    puntos_puntualidad = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    puntos_monto = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    puntos_antiguedad = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    puntos_frecuencia = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    puntos_devoluciones = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    puntos_incidentes = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    version_politica = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fecha_corte_historial = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    calculado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfil_crediticio", x => x.cliente_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "credito",
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
                name: "historial_cliente",
                schema: "credito");

            migrationBuilder.DropTable(
                name: "linea_credito",
                schema: "credito");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "credito");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "perfil_crediticio",
                schema: "credito");
        }
    }
}
