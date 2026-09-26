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

internal sealed class GetResourceMetricsRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    BlazorTelemetryOptions options,
    ILogger<GetResourceMetricsRequestHandler> logger) : IRequestHandler<GetResourceMetricsRequest, QueryResult<IReadOnlyList<TelemetryItem>>>
{
    public async Task<QueryResult<IReadOnlyList<TelemetryItem>>> Handle(GetResourceMetricsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetResourceMetrics(request.FromUtc, request.ToUtc, request.ServiceName, cancellationToken);
            return new QueryResult<IReadOnlyList<TelemetryItem>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetResourceMetrics failed.");
            return new QueryResult<IReadOnlyList<TelemetryItem>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<TelemetryItem>> GetResourceMetrics(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? serviceName, CancellationToken cancellationToken)
    {
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var _source = _context.TelemetryItems.AsNoTracking().Where(_item => _item.Kind == TelemetryKind.Metric
            && _item.TimestampUtc <= toUtc && _item.NumericValue.HasValue);
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            _source = _source.Where(_item => _item.ServiceName == serviceName);
        }

        var _result = new List<TelemetryItem>();
        // Share one budget, including baselines, across instruments. Unrelated instruments
        // cannot evict process metrics and historical/retired instances are never loaded.
        var _maximumRows = Math.Max(1, options?.MaximumDashboardMetricRows ?? 5_000);
        for (var _index = 0; _index < ResourceMetricNames.All.Count; _index++)
        {
            var _name = ResourceMetricNames.All[_index];
            var _budget = _maximumRows / ResourceMetricNames.All.Count
                + (_index < _maximumRows % ResourceMetricNames.All.Count ? 1 : 0);
            if (_budget == 0)
            {
                continue;
            }
            var _instrument = _source.Where(_item => _item.Name == _name);
            var _pointsQuery = _instrument.Where(_item => _item.TimestampUtc >= fromUtc)
                .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                .Take((_budget + 1) / 2);
            var _points = await _pointsQuery.Select(METRIC_PROJECTION).ToListAsync(cancellationToken);
            if (_points.Count == 0)
            {
                continue;
            }
            var _firstTimestamp = _points.Min(_item => _item.TimestampUtc);
            var _baselines = await MetricBaselines(_context.TelemetryItems, _instrument, _pointsQuery, _firstTimestamp)
                .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                .Take(_budget - _points.Count).Select(METRIC_PROJECTION).ToListAsync(cancellationToken);
            _result.AddRange(_baselines);
            _result.AddRange(_points);
        }
        return _result;
    }
}
