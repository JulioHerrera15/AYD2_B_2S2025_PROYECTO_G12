using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SgoLogic.Auth.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRolUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rol",
                schema: "auth",
                table: "usuarios",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "CLIENTE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rol",
                schema: "auth",
                table: "usuarios");
        }
    }
}
