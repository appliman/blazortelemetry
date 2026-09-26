namespace BlazorTelemetry.Core;

public sealed record DashboardBreakdown(
    IReadOnlyDictionary<string, long> HttpMethods,
    IReadOnlyDictionary<int, long> HttpStatuses,
    IReadOnlyDictionary<string, long> EntityFrameworkOperations)
{
    public static DashboardBreakdown Empty { get; } = new(
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<int, long>(),
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
}
