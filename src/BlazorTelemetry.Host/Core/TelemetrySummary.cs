namespace BlazorTelemetry.Core;

public sealed record TelemetrySummary(
    long Logs,
    long Traces,
    long Metrics,
    long Errors,
    double P95LatencyMs,
    int ActiveIncidents,
    DateTimeOffset? OldestUtc,
    DateTimeOffset? NewestUtc,
    IReadOnlyList<string> Services);
