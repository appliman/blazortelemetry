using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.Metrics;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class MetricExtensions
{
    public static async Task<IReadOnlyList<string>> GetMetricNames(this IMediator mediator, DateTimeOffset fromUtc, DateTimeOffset toUtc, string? serviceName, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetMetricNamesRequest(fromUtc, toUtc, serviceName), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IReadOnlyList<TelemetryItem>> GetResourceMetrics(this IMediator mediator, DateTimeOffset fromUtc, DateTimeOffset toUtc, string? serviceName, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetResourceMetricsRequest(fromUtc, toUtc, serviceName), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<BlazorDashboardMetrics> GetBlazorDashboardMetrics(this IMediator mediator, DateTimeOffset fromUtc, string? serviceName, CancellationToken cancellationToken, DateTimeOffset? toUtc = null)
    {
        var result = await mediator.Send(new GetBlazorDashboardMetricsRequest(fromUtc, serviceName, toUtc), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(this IMediator mediator, string name, string? serviceName, DateTimeOffset fromUtc, CancellationToken cancellationToken, DateTimeOffset? toUtc = null)
    {
        var result = await GetMetricSeries(mediator, new GetMetricSeriesRequest(name, serviceName, fromUtc, toUtc), cancellationToken);
        return result;
    }

    internal static async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(this IMediator _mediator, TelemetryItem _sample,
        DateTimeOffset _fromUtc, DateTimeOffset _toUtc, int _maximumPoints, CancellationToken _cancellationToken)
    {
        var _request = new GetMetricSeriesRequest(_sample.Name, _sample.ServiceName, _fromUtc, _toUtc)
        {
            Sample = _sample,
            MaximumPoints = _maximumPoints
        };
        var _result = await GetMetricSeries(_mediator, _request, _cancellationToken);
        return _result;
    }

    private static async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(IMediator mediator, GetMetricSeriesRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(request, cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

}
