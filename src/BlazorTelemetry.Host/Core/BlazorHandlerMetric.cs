namespace BlazorTelemetry.Core;

public sealed record BlazorHandlerMetric(
    string Component,
    string Method,
    string EventName,
    long Invocations,
    double AverageDurationMs,
    double P95DurationMs,
    long Errors);
