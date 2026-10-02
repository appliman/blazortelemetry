namespace BlazorTelemetry.Core;

public sealed class TelemetryLog
{
    public long Id { get; set; }
    public long ResourceId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ScopeName { get; set; }
    public string? ScopeVersion { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public string? Fingerprint { get; set; }
    public string? Body { get; set; }
    public string? SeverityText { get; set; }
    public int? SeverityNumber { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public long? Flags { get; set; }
    public long? DroppedAttributesCount { get; set; }
    public TelemetryResource Resource { get; set; } = null!;
}
