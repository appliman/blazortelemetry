using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorTelemetry.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ManageIngestionApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExpiresUtc",
                table: "IngestionApplications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevokedUtc",
                table: "IngestionApplications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UsageCount",
                table: "IngestionApplications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresUtc",
                table: "IngestionApplications");

            migrationBuilder.DropColumn(
                name: "RevokedUtc",
                table: "IngestionApplications");

            migrationBuilder.DropColumn(
                name: "UsageCount",
                table: "IngestionApplications");
        }
    }
}
