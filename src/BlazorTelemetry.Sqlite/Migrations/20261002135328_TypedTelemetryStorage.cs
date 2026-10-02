using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorTelemetry.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class TypedTelemetryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelemetryResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Environment = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceInstanceId = table.Column<string>(type: "TEXT", nullable: true),
                    SdkName = table.Column<string>(type: "TEXT", nullable: true),
                    SdkLanguage = table.Column<string>(type: "TEXT", nullable: true),
                    SdkVersion = table.Column<string>(type: "TEXT", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeName = table.Column<string>(type: "TEXT", nullable: true),
                    ScopeVersion = table.Column<string>(type: "TEXT", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    SeverityText = table.Column<string>(type: "TEXT", nullable: true),
                    SeverityNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", nullable: true),
                    Flags = table.Column<long>(type: "INTEGER", nullable: true),
                    DroppedAttributesCount = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryLogs_TelemetryResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "TelemetryResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryMetrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionsTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScopeName = table.Column<string>(type: "TEXT", nullable: true),
                    ScopeVersion = table.Column<string>(type: "TEXT", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: true),
                    MetricType = table.Column<string>(type: "TEXT", nullable: true),
                    NumericValue = table.Column<double>(type: "REAL", nullable: true),
                    StartTimeUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    AggregationTemporality = table.Column<string>(type: "TEXT", nullable: true),
                    IsMonotonic = table.Column<bool>(type: "INTEGER", nullable: true),
                    Count = table.Column<long>(type: "INTEGER", nullable: true),
                    Sum = table.Column<double>(type: "REAL", nullable: true),
                    Minimum = table.Column<double>(type: "REAL", nullable: true),
                    Maximum = table.Column<double>(type: "REAL", nullable: true),
                    Scale = table.Column<int>(type: "INTEGER", nullable: true),
                    ZeroCount = table.Column<long>(type: "INTEGER", nullable: true),
                    PositiveOffset = table.Column<int>(type: "INTEGER", nullable: true),
                    NegativeOffset = table.Column<int>(type: "INTEGER", nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryMetrics_TelemetryResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "TelemetryResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryRequest",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeName = table.Column<string>(type: "TEXT", nullable: true),
                    ScopeVersion = table.Column<string>(type: "TEXT", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", nullable: true),
                    ParentSpanId = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<double>(type: "REAL", nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    SpanKind = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    CircuitId = table.Column<string>(type: "TEXT", nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    Route = table.Column<string>(type: "TEXT", nullable: true),
                    ProtocolVersion = table.Column<string>(type: "TEXT", nullable: true),
                    UrlScheme = table.Column<string>(type: "TEXT", nullable: true),
                    ClientAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ClientPort = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ServerPort = table.Column<int>(type: "INTEGER", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryRequest_TelemetryResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "TelemetryResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryTraces",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CollectionsTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScopeName = table.Column<string>(type: "TEXT", nullable: true),
                    ScopeVersion = table.Column<string>(type: "TEXT", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", nullable: true),
                    ParentSpanId = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<double>(type: "REAL", nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    SpanKind = table.Column<string>(type: "TEXT", nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryTraces_TelemetryResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "TelemetryResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryMetricBuckets",
                columns: table => new
                {
                    MetricId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Group = table.Column<int>(type: "INTEGER", nullable: false),
                    UpperBound = table.Column<double>(type: "REAL", nullable: true),
                    Count = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMetricBuckets", x => new { x.MetricId, x.Ordinal, x.Group });
                    table.ForeignKey(
                        name: "FK_TelemetryMetricBuckets_TelemetryMetrics_MetricId",
                        column: x => x.MetricId,
                        principalTable: "TelemetryMetrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryMetricExemplars",
                columns: table => new
                {
                    MetricId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", nullable: true),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMetricExemplars", x => new { x.MetricId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_TelemetryMetricExemplars_TelemetryMetrics_MetricId",
                        column: x => x.MetricId,
                        principalTable: "TelemetryMetrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryMetricQuantiles",
                columns: table => new
                {
                    MetricId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantile = table.Column<double>(type: "REAL", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMetricQuantiles", x => new { x.MetricId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_TelemetryMetricQuantiles_TelemetryMetrics_MetricId",
                        column: x => x.MetricId,
                        principalTable: "TelemetryMetrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryTraceEvents",
                columns: table => new
                {
                    TraceRecordId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryTraceEvents", x => new { x.TraceRecordId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_TelemetryTraceEvents_TelemetryTraces_TraceRecordId",
                        column: x => x.TraceRecordId,
                        principalTable: "TelemetryTraces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryTraceLinks",
                columns: table => new
                {
                    TraceRecordId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryTraceLinks", x => new { x.TraceRecordId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_TelemetryTraceLinks_TelemetryTraces_TraceRecordId",
                        column: x => x.TraceRecordId,
                        principalTable: "TelemetryTraces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLogs_Fingerprint",
                table: "TelemetryLogs",
                column: "Fingerprint",
                unique: true,
                filter: "Fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLogs_ResourceId_TimestampUtc",
                table: "TelemetryLogs",
                columns: new[] { "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLogs_TimestampUtc",
                table: "TelemetryLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLogs_TraceId",
                table: "TelemetryLogs",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLogs_TraceId_SpanId",
                table: "TelemetryLogs",
                columns: new[] { "TraceId", "SpanId" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_Fingerprint",
                table: "TelemetryMetrics",
                column: "Fingerprint",
                unique: true,
                filter: "Fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_Name_ResourceId_TimestampUtc",
                table: "TelemetryMetrics",
                columns: new[] { "Name", "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_Name_TimestampUtc",
                table: "TelemetryMetrics",
                columns: new[] { "Name", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_ResourceId_TimestampUtc",
                table: "TelemetryMetrics",
                columns: new[] { "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetrics_TimestampUtc",
                table: "TelemetryMetrics",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRequest_Fingerprint",
                table: "TelemetryRequest",
                column: "Fingerprint",
                unique: true,
                filter: "Fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRequest_ResourceId_TimestampUtc",
                table: "TelemetryRequest",
                columns: new[] { "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRequest_TimestampUtc",
                table: "TelemetryRequest",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRequest_TraceId",
                table: "TelemetryRequest",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRequest_TraceId_SpanId",
                table: "TelemetryRequest",
                columns: new[] { "TraceId", "SpanId" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryResources_Hash",
                table: "TelemetryResources",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryResources_ServiceName_Id",
                table: "TelemetryResources",
                columns: new[] { "ServiceName", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraces_Fingerprint",
                table: "TelemetryTraces",
                column: "Fingerprint",
                unique: true,
                filter: "Fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraces_ResourceId_TimestampUtc",
                table: "TelemetryTraces",
                columns: new[] { "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraces_TimestampUtc",
                table: "TelemetryTraces",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraces_TraceId",
                table: "TelemetryTraces",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraces_TraceId_SpanId",
                table: "TelemetryTraces",
                columns: new[] { "TraceId", "SpanId" });
            migrationBuilder.DropTable(
                name: "TelemetryItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetryLogs");

            migrationBuilder.DropTable(
                name: "TelemetryMetricBuckets");

            migrationBuilder.DropTable(
                name: "TelemetryMetricExemplars");

            migrationBuilder.DropTable(
                name: "TelemetryMetricQuantiles");

            migrationBuilder.DropTable(
                name: "TelemetryRequest");

            migrationBuilder.DropTable(
                name: "TelemetryTraceEvents");

            migrationBuilder.DropTable(
                name: "TelemetryTraceLinks");

            migrationBuilder.DropTable(
                name: "TelemetryMetrics");

            migrationBuilder.DropTable(
                name: "TelemetryTraces");

            migrationBuilder.DropTable(
                name: "TelemetryResources");

            migrationBuilder.CreateTable(
                name: "TelemetryItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<double>(type: "REAL", nullable: true),
                    Environment = table.Column<string>(type: "TEXT", nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    MetricType = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NumericValue = table.Column<double>(type: "REAL", nullable: true),
                    ObservedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ParentSpanId = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceAttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ServiceVersion = table.Column<string>(type: "TEXT", nullable: true),
                    SeverityNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    SeverityText = table.Column<string>(type: "TEXT", nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Fingerprint",
                table: "TelemetryItems",
                column: "Fingerprint",
                unique: true,
                filter: "Fingerprint IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_Name_ServiceName_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "Name", "ServiceName", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_Name_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "Name", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryItems_Kind_ServiceName_TimestampUtc",
                table: "TelemetryItems",
                columns: new[] { "Kind", "ServiceName", "TimestampUtc" });

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
    }
}
