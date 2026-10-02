namespace BlazorTelemetry.Core;

public sealed class TelemetryMetricExemplar
{
    public long MetricId { get; set; }
    public int Ordinal { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public double Value { get; set; }
    public string AttributesJson { get; set; } = "{}";
}
