namespace BlazorTelemetry.Core;

public sealed class TelemetryTrace
{
    public long Id { get; set; }
    public long ResourceId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CollectionsTruncated { get; set; }
    public string? ScopeName { get; set; }
    public string? ScopeVersion { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public double? DurationMs { get; set; }
    public int? StatusCode { get; set; }
    public string? Body { get; set; }
    public string? SpanKind { get; set; }
    public string? Fingerprint { get; set; }
    public TelemetryResource Resource { get; set; } = null!;
}
