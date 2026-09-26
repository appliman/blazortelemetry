using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorTelemetry.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IndexHandlerQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_ServiceName_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "ServiceName", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionApplications_Name",
                table: "IngestionApplications",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_StartedUtc",
                table: "Incidents",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_Name",
                table: "Dashboards",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Name",
                table: "AlertRules",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetryItems_Kind_ServiceName_TimestampUtc",
                table: "TelemetryItems");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptUtc",
                table: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_IngestionApplications_Name",
                table: "IngestionApplications");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_StartedUtc",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Dashboards_Name",
                table: "Dashboards");

            migrationBuilder.DropIndex(
                name: "IX_AlertRules_Name",
                table: "AlertRules");
        }
    }
}
