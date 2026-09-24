using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.IntegracionErp.Worker.Infrastructure.Migraciones
{
    /// <inheritdoc />
    public partial class InicialIntegracionErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliaciones_erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LoteId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegistrosSgo = table.Column<int>(type: "integer", nullable: false),
                    MontoSgo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RegistrosErp = table.Column<int>(type: "integer", nullable: false),
                    MontoErp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Conciliada = table.Column<bool>(type: "boolean", nullable: false),
                    Diferencias = table.Column<string>(type: "text", nullable: true),
                    CreadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conciliaciones_erp", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lotes_erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Secuencia = table.Column<long>(type: "bigint", nullable: false),
                    FechaGeneracion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Formato = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CantidadRegistros = table.Column<int>(type: "integer", nullable: false),
                    MontoTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Contenido = table.Column<string>(type: "text", nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    CodigoHttp = table.Column<int>(type: "integer", nullable: true),
                    RespuestaErp = table.Column<string>(type: "text", nullable: true),
                    EnviadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lotes_erp", x => x.Id);
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
                name: "ventas_erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrdenId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NumeroOrden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tienda = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FechaVenta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DocumentoFiscalId = table.Column<string>(type: "text", nullable: true),
                    SerieFiscal = table.Column<string>(type: "text", nullable: true),
                    NumeroFiscal = table.Column<string>(type: "text", nullable: true),
                    DevolucionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TipoDevolucion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ventas_erp", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "registros_lote_erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoteErpId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tienda = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_lote_erp", x => x.Id);
                    table.ForeignKey(
                        name: "FK_registros_lote_erp_lotes_erp_LoteErpId",
                        column: x => x.LoteErpId,
                        principalTable: "lotes_erp",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conciliaciones_erp_Fecha_LoteId",
                table: "conciliaciones_erp",
                columns: new[] { "Fecha", "LoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_lotes_erp_LoteId",
                table: "lotes_erp",
                column: "LoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lotes_erp_Secuencia",
                table: "lotes_erp",
                column: "Secuencia",
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
                name: "IX_registros_lote_erp_LoteErpId",
                table: "registros_lote_erp",
                column: "LoteErpId");

            migrationBuilder.CreateIndex(
                name: "IX_ventas_erp_EventoId",
                table: "ventas_erp",
                column: "EventoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ventas_erp_OrdenId",
                table: "ventas_erp",
                column: "OrdenId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliaciones_erp");

            migrationBuilder.DropTable(
                name: "mensajes_procesados");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "registros_lote_erp");

            migrationBuilder.DropTable(
                name: "ventas_erp");

            migrationBuilder.DropTable(
                name: "lotes_erp");
        }
    }
}
