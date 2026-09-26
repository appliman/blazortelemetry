using System.ComponentModel;
using BlazorTelemetry.Core;
using ModelContextProtocol.Server;

namespace BlazorTelemetry.Host.Mcp;

[McpServerToolType]
public sealed class TelemetryMcpTools(IMediator repository)
{
    [McpServerTool(Name = "telemetry_services_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("List services that produced telemetry in a UTC time window.")]
    public Task<IReadOnlyList<string>> ListServices(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        return repository.GetServices(range.From, range.To, cancellationToken);
    }

    [McpServerTool(Name = "telemetry_summary", ReadOnly = true, UseStructuredContent = true)]
    [Description("Count logs, traces, metrics and errors, and return latency and services for a UTC time window.")]
    public Task<TelemetrySummary> GetSummary(string? serviceName = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        return repository.GetSummary(range.From, cancellationToken, range.To, serviceName);
    }

    [McpServerTool(Name = "telemetry_logs_query", ReadOnly = true, UseStructuredContent = true)]
    [Description("Search logs by service, text, trace ID, severity and UTC time. Results are paged; take is capped at 100.")]
    public Task<TelemetryPage> QueryLogs(string? serviceName = null, string? search = null, string? traceId = null,
        int? minimumSeverityNumber = null, int? maximumSeverityNumber = null, DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        return repository.Query(new TelemetryQuery(Kind: TelemetryKind.Log, ServiceName: serviceName, Search: search,
            TraceId: traceId, FromUtc: range.From, ToUtc: range.To, Skip: Math.Max(0, skip), Take: Math.Clamp(take, 1, 100),
            MinimumSeverityNumber: minimumSeverityNumber, MaximumSeverityNumber: maximumSeverityNumber), cancellationToken);
    }

    [McpServerTool(Name = "telemetry_errors_query", ReadOnly = true, UseStructuredContent = true)]
    [Description("Find error logs (OpenTelemetry severity >= 17) and failed spans (status 2) in a UTC time window.")]
    public async Task<object> QueryErrors(string? serviceName = null, string? search = null, DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        var pageSize = Math.Clamp(take, 1, 100);
        var offset = Math.Max(0, skip);
        var logs = await repository.Query(new TelemetryQuery(Kind: TelemetryKind.Log, ServiceName: serviceName, Search: search,
            FromUtc: range.From, ToUtc: range.To, Skip: offset, Take: pageSize, MinimumSeverityNumber: 17), cancellationToken);
        var traces = await repository.Query(new TelemetryQuery(Kind: TelemetryKind.Trace, ServiceName: serviceName, Search: search,
            FromUtc: range.From, ToUtc: range.To, Skip: offset, Take: pageSize, MinimumStatusCode: 2, MaximumStatusCode: 2), cancellationToken);
        return new { Logs = logs, Traces = traces };
    }

    [McpServerTool(Name = "telemetry_traces_query", ReadOnly = true, UseStructuredContent = true)]
    [Description("Search spans by service, text, trace ID, status code and UTC time. Results are paged; take is capped at 100.")]
    public Task<TelemetryPage> QueryTraces(string? serviceName = null, string? search = null, string? traceId = null,
        int? statusCode = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int skip = 0,
        int take = 50, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        return repository.Query(new TelemetryQuery(Kind: TelemetryKind.Trace, ServiceName: serviceName, Search: search,
            TraceId: traceId, FromUtc: range.From, ToUtc: range.To, Skip: Math.Max(0, skip), Take: Math.Clamp(take, 1, 100),
            MinimumStatusCode: statusCode, MaximumStatusCode: statusCode), cancellationToken);
    }

    [McpServerTool(Name = "telemetry_trace_get", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return logs and spans sharing an exact trace ID. Results are capped at 500 entries.")]
    public Task<TelemetryPage> GetTrace(string traceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(traceId) || traceId.Length > 32)
        {
            throw new ArgumentException("A valid trace ID is required.", nameof(traceId));
        }

        return repository.Query(new TelemetryQuery(TraceId: traceId, Take: 500), cancellationToken);
    }

    [McpServerTool(Name = "telemetry_metrics_query", ReadOnly = true, UseStructuredContent = true)]
    [Description("Search metric samples by exact name, service, text and UTC time. Results are paged; take is capped at 100.")]
    public Task<TelemetryPage> QueryMetrics(string? name = null, string? serviceName = null, string? search = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int skip = 0, int take = 50,
        CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        return repository.Query(new TelemetryQuery(Kind: TelemetryKind.Metric, ServiceName: serviceName, Search: search,
            FromUtc: range.From, ToUtc: range.To, Skip: Math.Max(0, skip), Take: Math.Clamp(take, 1, 100), Name: name), cancellationToken);
    }

    [McpServerTool(Name = "telemetry_metric_names_list", ReadOnly = true, UseStructuredContent = true)]
    [Description("List distinct metric names for a service and UTC time window, up to 500 names.")]
    public Task<IReadOnlyList<string>> ListMetricNames(string? serviceName = null, DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(fromUtc, toUtc);
        return repository.GetMetricNames(range.From, range.To, serviceName, cancellationToken);
    }

    [McpServerTool(Name = "telemetry_metric_series", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return up to 2000 numeric points of an exact metric name, ordered by UTC time.")]
    public Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(string name, string? serviceName = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A metric name is required.", nameof(name));
        }

        var range = ResolveRange(fromUtc, toUtc);
        return repository.GetMetricSeries(name, serviceName, range.From, cancellationToken, range.To);
    }

    private static (DateTimeOffset From, DateTimeOffset To) ResolveRange(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var to = (toUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var from = (fromUtc ?? to.AddHours(-1)).ToUniversalTime();
        if (from > to)
        {
            throw new ArgumentException("fromUtc must be earlier than or equal to toUtc.");
        }

        return (from, to);
    }
}
