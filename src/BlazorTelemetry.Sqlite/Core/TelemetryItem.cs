namespace BlazorTelemetry.Core;

public sealed class TelemetryItem
{
    public long Id { get; set; }
    public TelemetryKind Kind { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public string ServiceName { get; set; } = "service-inconnu";
    public string? ServiceVersion { get; set; }
    public string? Environment { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? SeverityText { get; set; }
    public int? SeverityNumber { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public double? DurationMs { get; set; }
    public int? StatusCode { get; set; }
    public string? Unit { get; set; }
    public string? MetricType { get; set; }
    public double? NumericValue { get; set; }
    public string ResourceAttributesJson { get; set; } = "{}";
    public string AttributesJson { get; set; } = "{}";
    public string DetailsJson { get; set; } = "{}";
    public string? Fingerprint { get; set; }
}
