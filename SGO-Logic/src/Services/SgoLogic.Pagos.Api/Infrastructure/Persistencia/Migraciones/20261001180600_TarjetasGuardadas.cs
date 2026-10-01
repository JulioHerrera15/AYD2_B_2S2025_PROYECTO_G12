using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class TarjetasGuardadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tarjeta_guardada",
                schema: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    referencia_payklik = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    marca = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ultimos4 = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    mes_exp = table.Column<int>(type: "integer", nullable: false),
                    anio_exp = table.Column<int>(type: "integer", nullable: false),
                    titular = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarjeta_guardada", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tarjeta_guardada_cliente_tarjeta_activa",
                schema: "pagos",
                table: "tarjeta_guardada",
                columns: new[] { "cliente_id", "marca", "ultimos4", "mes_exp", "anio_exp" },
                unique: true,
                filter: "activa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tarjeta_guardada",
                schema: "pagos");
        }
    }
}
