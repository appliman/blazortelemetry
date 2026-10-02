namespace BlazorTelemetry.Core;

public sealed class TelemetryMetric
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
    public string? Body { get; set; }
    public string? Unit { get; set; }
    public string? MetricType { get; set; }
    public double? NumericValue { get; set; }
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
    public string? Fingerprint { get; set; }
    public TelemetryResource Resource { get; set; } = null!;
}
