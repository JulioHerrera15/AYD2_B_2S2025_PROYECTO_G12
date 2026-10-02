using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class BovedaTokenPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "token_pago",
                schema: "pagos",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    marca = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ultimos4 = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    mes_exp = table.Column<int>(type: "integer", nullable: false),
                    anio_exp = table.Column<int>(type: "integer", nullable: false),
                    titular = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    datos_cifrados = table.Column<byte[]>(type: "bytea", nullable: true),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    tag = table.Column<byte[]>(type: "bytea", nullable: true),
                    referencia_tarjeta_guardada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guardar_al_cobrar = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_pago", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_token_pago_estado_expira_en",
                schema: "pagos",
                table: "token_pago",
                columns: new[] { "estado", "expira_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "token_pago",
                schema: "pagos");
        }
    }
}
