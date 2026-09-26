namespace BlazorTelemetry.Core;

public sealed record BlazorRouteMetric(
    string Route,
    string Component,
    long Navigations,
    long Errors);
