using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Notificaciones.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InicialNotificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notificaciones");

            migrationBuilder.CreateTable(
                name: "mensajes_procesados",
                schema: "notificaciones",
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

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_procesados_event_id_nombre_servicio",
                schema: "notificaciones",
                table: "mensajes_procesados",
                columns: new[] { "event_id", "nombre_servicio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensajes_procesados",
                schema: "notificaciones");
        }
    }
}
