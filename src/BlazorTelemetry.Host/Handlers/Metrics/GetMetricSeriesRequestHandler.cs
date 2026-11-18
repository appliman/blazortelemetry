using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
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
            var value = await GetMetricSeries(request, cancellationToken);
            return new QueryResult<IReadOnlyList<MetricSeriesPoint>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetMetricSeries failed.");
            return new QueryResult<IReadOnlyList<MetricSeriesPoint>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(GetMetricSeriesRequest request, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var name = request.Name;
        var fromUtc = request.FromUtc;
        var toUtc = request.ToUtc;
        var serviceName = request.ServiceName;
        var source = context.QueryTelemetry(TelemetryKind.Metric).AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric && item.Name == name && item.TimestampUtc >= fromUtc && item.NumericValue.HasValue);
        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        var _sample = request.Sample;
        IOrderedQueryable<TelemetryItem> _ordered;
        if (_sample is not null)
        {
            var _scope = _sample.ScopeName;
            source = source.Where(_item => _item.ResourceId == _sample.ResourceId
                && _item.AttributesJson == _sample.AttributesJson
                && _item.MetricType == _sample.MetricType && _item.Unit == _sample.Unit);
            source = string.IsNullOrEmpty(_scope)
                ? source.Where(_item => _item.ScopeName == null || _item.ScopeName == string.Empty)
                : source.Where(_item => _item.ScopeName == _scope);
            _ordered = source.OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id);
        }
        else
        {
            _ordered = source.OrderBy(_item => _item.TimestampUtc);
        }

        var _result = await _ordered.Take(request.MaximumPoints)
            .Select(_item => new MetricSeriesPoint(_item.TimestampUtc, _item.NumericValue!.Value))
            .ToListAsync(cancellationToken);
        if (_sample is not null)
        {
            _result.Reverse();
        }
        return _result;
    }
}
