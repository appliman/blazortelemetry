namespace BlazorTelemetry.Core;

public sealed class TelemetryMetricQuantile
{
    public long MetricId { get; set; }
    public int Ordinal { get; set; }
    public double Quantile { get; set; }
    public double Value { get; set; }
}
