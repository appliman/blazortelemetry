using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorTelemetry.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SecureAuthenticationPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAutoLogin",
                table: "IngestionApplications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AdministratorTotpStates",
                columns: table => new
                {
                    Identifier = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LastAcceptedInterval = table.Column<long>(type: "INTEGER", nullable: false),
                    SecretVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministratorTotpStates", x => x.Identifier);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministratorTotpStates");

            migrationBuilder.DropColumn(
                name: "AllowAutoLogin",
                table: "IngestionApplications");
        }
    }
}
