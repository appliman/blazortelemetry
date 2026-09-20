namespace BlazorTelemetry.Core;

public sealed record TelemetryQuery(
    TelemetryKind? Kind = null,
    string? ServiceName = null,
    string? Search = null,
    string? TraceId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Skip = 0,
    int Take = 100,
    int? MinimumSeverityNumber = null,
    int? MaximumSeverityNumber = null,
    string? ExcludedServiceName = null,
    int? MinimumStatusCode = null,
    int? MaximumStatusCode = null,
    bool? HasStatusCode = null);
