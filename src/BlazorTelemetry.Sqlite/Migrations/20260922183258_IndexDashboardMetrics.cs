using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorTelemetry.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IndexDashboardMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_Name_ServiceName_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "Name", "ServiceName", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_Name_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "Name", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetryItems_Kind_Name_ServiceName_TimestampUtc",
                table: "TelemetryItems");

            migrationBuilder.DropIndex(
                name: "IX_TelemetryItems_Kind_Name_TimestampUtc",
                table: "TelemetryItems");
        }
    }
}
