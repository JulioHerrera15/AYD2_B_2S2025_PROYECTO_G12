using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialFacturacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "facturacion");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateSequence(
                name: "documento_fiscal_correlativo",
                schema: "facturacion");

            migrationBuilder.CreateTable(
                name: "documento_fiscal",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    documento_origen_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    cui_receptor = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    nombre_receptor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    direccion_receptor = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    pais_receptor = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    impuestos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    serie = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    correlativo = table.Column<long>(type: "bigint", nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xml_generado = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    folio = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    numero_autorizacion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ultimo_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ultimo_motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    certificado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documento_fiscal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "facturacion",
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
                name: "documento_fiscal_linea",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_linea = table.Column<int>(type: "integer", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    impuesto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    documento_fiscal_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documento_fiscal_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_fiscal_linea_documento",
                        column: x => x.documento_fiscal_id,
                        principalSchema: "facturacion",
                        principalTable: "documento_fiscal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_estado_proximo_intento_en",
                schema: "facturacion",
                table: "documento_fiscal",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_orden_id",
                schema: "facturacion",
                table: "documento_fiscal",
                column: "orden_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_serie_correlativo",
                schema: "facturacion",
                table: "documento_fiscal",
                columns: new[] { "serie", "correlativo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_fiscal_linea_documento_numero",
                schema: "facturacion",
                table: "documento_fiscal_linea",
                columns: new[] { "documento_fiscal_id", "numero_linea" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "facturacion",
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
                name: "documento_fiscal_linea",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "documento_fiscal",
                schema: "facturacion");

            migrationBuilder.DropSequence(
                name: "documento_fiscal_correlativo",
                schema: "facturacion");
        }
    }
}
