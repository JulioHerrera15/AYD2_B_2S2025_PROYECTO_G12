using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.ConsultaEstado.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InicialConsultaEstado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consulta_estado");

            migrationBuilder.CreateTable(
                name: "incidencias",
                schema: "consulta_estado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resolucion = table.Column<string>(type: "text", nullable: true),
                    creada_en = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    actualizada_en = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidencias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "consulta_estado",
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
                name: "ordenes_vista",
                schema: "consulta_estado",
                columns: table => new
                {
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    numero_orden = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cliente_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    estado = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    estado_anterior = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    metodo_entrega = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ubicacion_solicitada_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    impuestos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pago_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tienda_destino_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    documento_fiscal_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    serie_documento_fiscal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    numero_documento_fiscal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    motivo_ultimo_cambio = table.Column<string>(type: "text", nullable: true),
                    requiere_reembolso = table.Column<bool>(type: "boolean", nullable: true),
                    monto_reembolso = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ultimo_sequence = table.Column<long>(type: "bigint", nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    actualizada_en = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordenes_vista", x => x.orden_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_orden_id",
                schema: "consulta_estado",
                table: "incidencias",
                column: "orden_id");

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "consulta_estado",
                table: "mensajes_procesados",
                columns: new[] { "event_id", "nombre_servicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_vista_cliente_id",
                schema: "consulta_estado",
                table: "ordenes_vista",
                column: "cliente_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidencias",
                schema: "consulta_estado");

            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "consulta_estado");

            migrationBuilder.DropTable(
                name: "ordenes_vista",
                schema: "consulta_estado");
        }
    }
}
