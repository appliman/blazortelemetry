using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.Metrics;

namespace BlazorTelemetry.Host.Handlers.Metrics;

internal sealed class GetMetricSeriesRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetMetricSeriesRequestHandler> logger) : IRequestHandler<GetMetricSeriesRequest, QueryResult<IReadOnlyList<MetricSeriesPoint>>>
{
    public async Task<QueryResult<IReadOnlyList<MetricSeriesPoint>>> Handle(GetMetricSeriesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetMetricSeries(request.Name, request.ServiceName, request.FromUtc, cancellationToken, request.ToUtc);
            return new QueryResult<IReadOnlyList<MetricSeriesPoint>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetMetricSeries failed.");
            return new QueryResult<IReadOnlyList<MetricSeriesPoint>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(string name, string? serviceName, DateTimeOffset fromUtc, CancellationToken cancellationToken, DateTimeOffset? toUtc = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric && item.Name == name && item.TimestampUtc >= fromUtc && item.NumericValue.HasValue);
        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        var values = await source.OrderBy(item => item.TimestampUtc).Take(2_000)
            .Select(item => new { item.TimestampUtc, Value = item.NumericValue!.Value })
            .ToListAsync(cancellationToken);
        return values.Select(value => new MetricSeriesPoint(value.TimestampUtc, value.Value)).ToList();
    }
}
