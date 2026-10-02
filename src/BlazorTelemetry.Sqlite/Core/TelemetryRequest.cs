namespace BlazorTelemetry.Core;

public sealed class TelemetryRequest
{
    public long Id { get; set; }
    public long ResourceId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ScopeName { get; set; }
    public string? ScopeVersion { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public double? DurationMs { get; set; }
    public int? StatusCode { get; set; }
    public string? SpanKind { get; set; }
    public string? Source { get; set; }
    public string? CircuitId { get; set; }
    public string? HttpMethod { get; set; }
    public string? Url { get; set; }
    public string? Route { get; set; }
    public string? ProtocolVersion { get; set; }
    public string? UrlScheme { get; set; }
    public string? ClientAddress { get; set; }
    public int? ClientPort { get; set; }
    public string? ServerAddress { get; set; }
    public int? ServerPort { get; set; }
    public string? UserAgent { get; set; }
    public string? Fingerprint { get; set; }
    public TelemetryResource Resource { get; set; } = null!;
}
