using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

namespace BlazorTelemetry.Host.Handlers.TelemetryItems;

internal sealed class GetSummaryRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetSummaryRequestHandler> logger) : IRequestHandler<GetSummaryRequest, QueryResult<TelemetrySummary>>
{
    public async Task<QueryResult<TelemetrySummary>> Handle(GetSummaryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetSummary(request.FromUtc, cancellationToken, request.ToUtc, request.ServiceName);
            return new QueryResult<TelemetrySummary> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetSummary failed.");
            return new QueryResult<TelemetrySummary> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<TelemetrySummary> GetSummary(DateTimeOffset fromUtc, CancellationToken cancellationToken, DateTimeOffset? toUtc = null, string? serviceName = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking().Where(item => item.TimestampUtc >= fromUtc);
        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(_item => _item.ServiceName == serviceName);
        }
        var logs = await source.LongCountAsync(item => item.Kind == TelemetryKind.Log, cancellationToken);
        var traces = await source.LongCountAsync(item => item.Kind == TelemetryKind.Trace, cancellationToken);
        var metrics = await source.LongCountAsync(item => item.Kind == TelemetryKind.Metric, cancellationToken);
        var errors = await source.LongCountAsync(item => item.StatusCode == 2 || (item.SeverityNumber ?? 0) >= 17, cancellationToken);
        var durations = await source.Where(item => item.Kind == TelemetryKind.Trace && item.DurationMs.HasValue)
            .OrderBy(item => item.DurationMs)
            .Select(item => item.DurationMs!.Value)
            .Take(10_000)
            .ToListAsync(cancellationToken);
        var p95 = durations.Count == 0 ? 0 : durations[(int)Math.Floor((durations.Count - 1) * 0.95)];
        var activeIncidents = await context.Incidents.CountAsync(incident => incident.State != IncidentState.Resolved, cancellationToken);
        var oldest = await source.OrderBy(item => item.TimestampUtc).Select(item => (DateTimeOffset?)item.TimestampUtc).FirstOrDefaultAsync(cancellationToken);
        var newest = await source.OrderByDescending(item => item.TimestampUtc).Select(item => (DateTimeOffset?)item.TimestampUtc).FirstOrDefaultAsync(cancellationToken);
        var services = await source.Select(item => item.ServiceName).Distinct().OrderBy(name => name).Take(100).ToListAsync(cancellationToken);
        return new(logs, traces, metrics, errors, p95, activeIncidents, oldest, newest, services);
    }
}
