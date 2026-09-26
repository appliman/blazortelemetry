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

internal sealed class GetBlazorDashboardMetricsRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    BlazorTelemetryOptions options,
    ILogger<GetBlazorDashboardMetricsRequestHandler> logger) : IRequestHandler<GetBlazorDashboardMetricsRequest, QueryResult<BlazorDashboardMetrics>>
{
    public async Task<QueryResult<BlazorDashboardMetrics>> Handle(GetBlazorDashboardMetricsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetBlazorDashboardMetrics(request.FromUtc, request.ServiceName, cancellationToken, request.ToUtc);
            return new QueryResult<BlazorDashboardMetrics> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetBlazorDashboardMetrics failed.");
            return new QueryResult<BlazorDashboardMetrics> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<BlazorDashboardMetrics> GetBlazorDashboardMetrics(
        DateTimeOffset fromUtc,
        string? serviceName,
        CancellationToken cancellationToken,
        DateTimeOffset? toUtc = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric
                && item.TimestampUtc >= fromUtc
                && BLAZOR_METRIC_NAMES.Contains(item.Name));

        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        var maximumRows = Math.Max(1, options?.MaximumDashboardMetricRows ?? 5_000);
        var metrics = await source
            .OrderByDescending(item => item.TimestampUtc)
            .Take(maximumRows)
            .Select(item => new TelemetryItem
            {
                TimestampUtc = item.TimestampUtc,
                ServiceName = item.ServiceName,
                Name = item.Name,
                Unit = item.Unit,
                MetricType = item.MetricType,
                NumericValue = item.NumericValue,
                ResourceAttributesJson = item.ResourceAttributesJson,
                AttributesJson = item.AttributesJson,
                DetailsJson = item.DetailsJson
            })
            .ToListAsync(cancellationToken);
        metrics.Reverse();
        if (metrics.Count == 0)
        {
            return BlazorDashboardMetrics.Empty;
        }

        var activeCircuitMetrics = SelectMetrics(metrics, "aspnetcore.components.circuit.active");
        var connectedCircuitMetrics = SelectMetrics(metrics, "aspnetcore.components.circuit.connected");
        var navigationMetrics = SelectMetrics(metrics, NAVIGATION_METRIC_NAMES);
        var eventHandlerMetrics = SelectMetrics(metrics, EVENT_HANDLER_METRIC_NAMES);
        var updateParametersMetrics = SelectMetrics(metrics, UPDATE_PARAMETERS_METRIC_NAMES);
        var renderDiffMetrics = SelectMetrics(metrics, RENDER_DIFF_METRIC_NAMES);
        var circuitDurationMetrics = SelectMetrics(metrics, "aspnetcore.components.circuit.duration");

        var routes = navigationMetrics
            .GroupBy(item => new
            {
                Route = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.route") ?? "Unknown route",
                Component = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.type") ?? "Unknown component"
            })
            .Select(group => new BlazorRouteMetric(
                group.Key.Route,
                group.Key.Component,
                Convert.ToInt64(GetCounterDelta(group), System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToInt64(GetMetricEventCount(group.Where(HasError)), System.Globalization.CultureInfo.InvariantCulture)))
            .Where(item => item.Navigations > 0 || item.Errors > 0)
            .OrderByDescending(item => item.Navigations)
            .ThenBy(item => item.Route)
            .Take(10)
            .ToArray();

        var handlers = eventHandlerMetrics
            .GroupBy(item => new
            {
                Component = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.type") ?? "Unknown component",
                Method = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.method", "code.function.name") ?? "Unknown method",
                EventName = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.attribute.name") ?? "event"
            })
            .Select(group =>
            {
                var histogram = AggregateHistogram(group);
                return new BlazorHandlerMetric(
                    group.Key.Component,
                    group.Key.Method,
                    group.Key.EventName,
                    histogram.Count,
                    histogram.Count == 0 ? 0 : histogram.Sum / histogram.Count,
                    histogram.P95,
                    Convert.ToInt64(GetMetricEventCount(group.Where(HasError)), System.Globalization.CultureInfo.InvariantCulture));
            })
            .Where(item => item.Invocations > 0)
            .OrderByDescending(item => item.P95DurationMs)
            .ThenByDescending(item => item.Invocations)
            .Take(10)
            .ToArray();

        var circuitDuration = AggregateHistogram(circuitDurationMetrics);
        var eventHandlerDuration = AggregateHistogram(eventHandlerMetrics);
        var updateParametersDuration = AggregateHistogram(updateParametersMetrics);
        var renderDiffDuration = AggregateHistogram(renderDiffMetrics);

        return new BlazorDashboardMetrics(
            GetLatestGaugeValue(activeCircuitMetrics),
            GetLatestGaugeValue(connectedCircuitMetrics),
            Convert.ToInt64(GetCounterDelta(navigationMetrics), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt64(GetMetricEventCount(metrics.Where(HasError)), System.Globalization.CultureInfo.InvariantCulture),
            circuitDuration.Count == 0 ? null : circuitDuration.P95,
            eventHandlerDuration.Count == 0 ? null : eventHandlerDuration.P95,
            updateParametersDuration.Count == 0 ? null : updateParametersDuration.P95,
            renderDiffDuration.Count == 0 ? null : renderDiffDuration.P95,
            BuildGaugeSeries(activeCircuitMetrics),
            BuildGaugeSeries(connectedCircuitMetrics),
            routes,
            handlers);
    }
}
