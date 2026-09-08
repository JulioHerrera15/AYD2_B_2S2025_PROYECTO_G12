using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicialOrdenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ordenes");

            migrationBuilder.CreateTable(
                name: "mensaje_saliente",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agregado_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    publicado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensaje_saliente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orden",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    numero_orden = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    metodo_entrega = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ubicacion_solicitada_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    impuestos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orden", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "linea_orden",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linea_orden", x => x.id);
                    table.ForeignKey(
                        name: "fk_linea_orden_orden",
                        column: x => x.orden_id,
                        principalSchema: "ordenes",
                        principalTable: "orden",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transicion_orden",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    estado_anterior = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    estado_nuevo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paso_saga = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    es_compensacion = table.Column<bool>(type: "boolean", nullable: false),
                    cambiado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transicion_orden", x => x.id);
                    table.ForeignKey(
                        name: "fk_transicion_orden_orden",
                        column: x => x.orden_id,
                        principalSchema: "ordenes",
                        principalTable: "orden",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_linea_orden_orden_id",
                schema: "ordenes",
                table: "linea_orden",
                column: "orden_id");

            migrationBuilder.CreateIndex(
                name: "ix_mensaje_saliente_estado_creado_en",
                schema: "ordenes",
                table: "mensaje_saliente",
                columns: new[] { "estado", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_orden_numero_orden",
                schema: "ordenes",
                table: "orden",
                column: "numero_orden",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transicion_orden_orden_id_secuencia",
                schema: "ordenes",
                table: "transicion_orden",
                columns: new[] { "orden_id", "secuencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linea_orden",
                schema: "ordenes");

            migrationBuilder.DropTable(
                name: "mensaje_saliente",
                schema: "ordenes");

            migrationBuilder.DropTable(
                name: "transicion_orden",
                schema: "ordenes");

            migrationBuilder.DropTable(
                name: "orden",
                schema: "ordenes");
        }
    }
}
