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
        var _source = _context.QueryTelemetry(TelemetryKind.Metric).AsNoTracking().Where(_item => _item.Kind == TelemetryKind.Metric
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
            if (_name == "http.server.active_requests" && toUtc > fromUtc)
            {
                if (!await _instrument.AnyAsync(_item => _item.TimestampUtc >= fromUtc, cancellationToken))
                {
                    continue;
                }
                var _durationTicks = (toUtc - fromUtc).Ticks;
                var _bucketCount = (int)Math.Min(Math.Min(40, _budget), _durationTicks);
                for (var _bucket = 0; _bucket < _bucketCount; _bucket++)
                {
                    var _start = fromUtc.AddTicks(_durationTicks / _bucketCount * _bucket);
                    var _end = _bucket == _bucketCount - 1
                        ? toUtc
                        : fromUtc.AddTicks(_durationTicks / _bucketCount * (_bucket + 1));
                    var _isLastBucket = _bucket == _bucketCount - 1;
                    var _bucketBudget = _budget / _bucketCount + (_bucket < _budget % _bucketCount ? 1 : 0);
                    var _samples = await _instrument
                        .Where(_item => _item.TimestampUtc >= _start
                            && (_item.TimestampUtc < _end || _isLastBucket && _item.TimestampUtc == _end))
                        .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                        .Take(_bucketBudget).Select(METRIC_PROJECTION).ToListAsync(cancellationToken);
                    _result.AddRange(_samples);
                }
                continue;
            }
            var _pointsQuery = _instrument.Where(_item => _item.TimestampUtc >= fromUtc)
                .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                .Take((_budget + 1) / 2);
            var _points = await _pointsQuery.Select(METRIC_PROJECTION).ToListAsync(cancellationToken);
            if (_points.Count == 0)
            {
                continue;
            }
            var _firstTimestamp = _points.Min(_item => _item.TimestampUtc);
            var _baselines = await MetricBaselines(_context.QueryTelemetry(TelemetryKind.Metric), _instrument, _pointsQuery, _firstTimestamp)
                .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                .Take(_budget - _points.Count).Select(METRIC_PROJECTION).ToListAsync(cancellationToken);
            _result.AddRange(_baselines);
            _result.AddRange(_points);
        }
        return _result;
    }
}
