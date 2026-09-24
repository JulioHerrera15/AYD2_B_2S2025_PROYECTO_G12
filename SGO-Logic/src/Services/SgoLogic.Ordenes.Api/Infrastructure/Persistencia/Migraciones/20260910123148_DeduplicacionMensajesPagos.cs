using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DeduplicacionMensajesPagos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mensaje_procesado",
                schema: "ordenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensaje_procesado", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mensaje_procesado_message_id",
                schema: "ordenes",
                table: "mensaje_procesado",
                column: "message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensaje_procesado",
                schema: "ordenes");
        }
    }
}
