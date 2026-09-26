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

internal sealed class GetDashboardBreakdownRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetDashboardBreakdownRequestHandler> logger) : IRequestHandler<GetDashboardBreakdownRequest, QueryResult<DashboardBreakdown>>
{
    public async Task<QueryResult<DashboardBreakdown>> Handle(GetDashboardBreakdownRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetDashboardBreakdown(request.FromUtc, request.ServiceName, request.ExcludedRequestServiceName, cancellationToken, request.ToUtc);
            return new QueryResult<DashboardBreakdown> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetDashboardBreakdown failed.");
            return new QueryResult<DashboardBreakdown> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<DashboardBreakdown> GetDashboardBreakdown(
        DateTimeOffset fromUtc,
        string? serviceName,
        string? excludedRequestServiceName,
        CancellationToken cancellationToken,
        DateTimeOffset? toUtc = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Request && item.TimestampUtc >= fromUtc);
        var entityFrameworkMetrics = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric
                && item.Name == "blazortelemetry.entity_framework.commands"
                && item.NumericValue.HasValue);

        if (toUtc.HasValue)
        {
            requests = requests.Where(_item => _item.TimestampUtc <= toUtc.Value);
            entityFrameworkMetrics = entityFrameworkMetrics.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            requests = requests.Where(item => item.ServiceName == serviceName);
            entityFrameworkMetrics = entityFrameworkMetrics.Where(item => item.ServiceName == serviceName);
        }

        if (!string.IsNullOrWhiteSpace(excludedRequestServiceName))
        {
            requests = requests.Where(item => item.ServiceName != excludedRequestServiceName);
        }

        var methodRows = await requests
            .Where(item => item.Body != null && item.Body != string.Empty)
            .GroupBy(item => item.Body!)
            .Select(group => new { Method = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var statusRows = await requests
            .Where(item => item.StatusCode.HasValue)
            .GroupBy(item => item.StatusCode!.Value)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var _window = entityFrameworkMetrics.Where(_item => _item.TimestampUtc >= fromUtc);
        var _baselines = await MetricBaselines(context.TelemetryItems, entityFrameworkMetrics, _window, fromUtc)
            .Select(METRIC_PROJECTION)
            .ToListAsync(cancellationToken);
        var _previous = _baselines.GroupBy(MetricCounter.SeriesKey).ToDictionary(_group => _group.Key, _group => _group.MaxBy(_item => _item.TimestampUtc)!);
        var operations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await foreach (var _item in _window
            .OrderBy(_item => _item.TimestampUtc).ThenBy(_item => _item.Id)
            .Select(METRIC_PROJECTION).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var _key = MetricCounter.SeriesKey(_item);
            var _increment = MetricCounter.Increment(_item, _previous.GetValueOrDefault(_key), fromUtc);
            var _operation = ReadMetricAttribute(_item.AttributesJson, "db.operation.type");
            if (_operation is not null)
            {
                operations.TryAdd(_operation, 0);
                if (_increment.HasValue)
                {
                    operations[_operation] += Convert.ToInt64(_increment.Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            _previous[_key] = _item;
        }

        var methods = methodRows
            .GroupBy(item => item.Method.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count), StringComparer.OrdinalIgnoreCase);
        var statuses = statusRows.ToDictionary(item => item.Status, item => item.Count);

        return new DashboardBreakdown(methods, statuses, operations);
    }
}
