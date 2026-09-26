namespace BlazorTelemetry.Core;

public sealed record MetricSeriesPoint(DateTimeOffset TimestampUtc, double Value);
