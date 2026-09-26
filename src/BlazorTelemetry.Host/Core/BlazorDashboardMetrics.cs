namespace BlazorTelemetry.Core;

public sealed record BlazorDashboardMetrics(
    long ActiveCircuits,
    long ConnectedCircuits,
    long Navigations,
    long Errors,
    double? CircuitDurationP95Ms,
    double? EventHandlerP95Ms,
    double? ComponentUpdateP95Ms,
    double? RenderDiffP95Ms,
    IReadOnlyList<MetricSeriesPoint> ActiveCircuitsSeries,
    IReadOnlyList<MetricSeriesPoint> ConnectedCircuitsSeries,
    IReadOnlyList<BlazorRouteMetric> TopRoutes,
    IReadOnlyList<BlazorHandlerMetric> SlowestEventHandlers)
{
    public static BlazorDashboardMetrics Empty { get; } = new(
        0,
        0,
        0,
        0,
        null,
        null,
        null,
        null,
        [],
        [],
        [],
        []);
}
