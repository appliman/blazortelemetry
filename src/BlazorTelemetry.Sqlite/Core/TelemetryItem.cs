namespace BlazorTelemetry.Core;

public sealed class TelemetryItem
{
    public long Id { get; set; }
    public TelemetryKind Kind { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateTimeOffset ObservedUtc { get; set; }
    public string ServiceName { get; set; } = "unknown-service";
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
    public string? Fingerprint { get; set; }
    public long? Flags { get; set; }
    public long? DroppedAttributesCount { get; set; }
    public string? SpanKind { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public string? AggregationTemporality { get; set; }
    public bool? IsMonotonic { get; set; }
    public long? Count { get; set; }
    public double? Sum { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public int? Scale { get; set; }
    public long? ZeroCount { get; set; }
    public int? PositiveOffset { get; set; }
    public int? NegativeOffset { get; set; }
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
    public long ResourceId { get; set; }
    public bool CollectionsTruncated { get; set; }
    public string? ScopeName { get; set; }
    public string? ScopeVersion { get; set; }
    public string? ServiceInstanceId { get; set; }
    public string? SdkName { get; set; }
    public string? SdkLanguage { get; set; }
    public string? SdkVersion { get; set; }
    public List<TelemetryTraceEvent> Events { get; set; } = [];
    public List<TelemetryTraceLink> Links { get; set; } = [];
    public List<TelemetryMetricBucket> Buckets { get; set; } = [];
    public List<TelemetryMetricQuantile> Quantiles { get; set; } = [];
    public List<TelemetryMetricExemplar> Exemplars { get; set; } = [];
}
