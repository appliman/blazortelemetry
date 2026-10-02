namespace BlazorTelemetry.Core;

public sealed class TelemetryMetricBucket
{
    public long MetricId { get; set; }
    public int Ordinal { get; set; }
    public int Group { get; set; }
    public double? UpperBound { get; set; }
    public long Count { get; set; }
}
