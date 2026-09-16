using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorTelemetry.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialTelemetrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: true),
                    Threshold = table.Column<double>(type: "REAL", nullable: false),
                    WindowMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    NotifyWebhook = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyEmail = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyNtfy = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SilencedUntilUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dashboards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dashboards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlertRuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedValue = table.Column<double>(type: "REAL", nullable: false),
                    Threshold = table.Column<double>(type: "REAL", nullable: false),
                    StartedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastEvaluatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ResolvedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    AcknowledgedUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IncidentId = table.Column<long>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", nullable: false),
                    EventKey = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    SentUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ServiceVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Environment = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    SeverityText = table.Column<string>(type: "TEXT", nullable: true),
                    SeverityNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ParentSpanId = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<double>(type: "REAL", nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: true),
                    MetricType = table.Column<string>(type: "TEXT", nullable: true),
                    NumericValue = table.Column<double>(type: "REAL", nullable: true),
                    ResourceAttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_AlertRuleId_State",
                table: "Incidents",
                columns: new[] { "AlertRuleId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionApplications_KeyHash",
                table: "IngestionApplications",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_EventKey",
                table: "NotificationDeliveries",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Fingerprint",
                table: "TelemetryItems",
                column: "Fingerprint",
                unique: true,
                filter: "Fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_ServiceName_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "ServiceName", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_TimestampUtc",
                table: "TelemetryItems",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_TraceId",
                table: "TelemetryItems",
                column: "TraceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRules");

            migrationBuilder.DropTable(
                name: "Dashboards");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "IngestionApplications");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "TelemetryItems");
        }
    }
}
