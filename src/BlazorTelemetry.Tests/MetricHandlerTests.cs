using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using ChannelMediator;
using ChannelMediator.InMemory;
using BlazorTelemetry.Host.ModelExtensions;
using BlazorTelemetry.Host.Contracts.Models.Maintenance;
using BlazorTelemetry.Host.Validators;
using FluentValidation;

namespace BlazorTelemetry.Tests;

public sealed partial class TelemetryCqrsTests
{
    [Fact]
    public async Task MetricNameFilterAndNamesRespectServiceAndTime()
    {
        var repository = _mediator;
        var from = DateTimeOffset.Parse("2026-09-26T10:00:00Z");
        await repository.Store([
            new() { Kind = TelemetryKind.Metric, ServiceName = "app-a", Name = "cpu", TimestampUtc = from.AddMinutes(1) },
            new() { Kind = TelemetryKind.Metric, ServiceName = "app-a", Name = "memory", TimestampUtc = from.AddMinutes(1) },
            new() { Kind = TelemetryKind.Metric, ServiceName = "app-b", Name = "disk", TimestampUtc = from.AddMinutes(1) },
            new() { Kind = TelemetryKind.Metric, ServiceName = "app-a", Name = "late", TimestampUtc = from.AddHours(2) }
        ], CancellationToken.None);

        Assert.Equal(new[] { "cpu", "memory" }, await repository.GetMetricNames(from, from.AddHours(1), "app-a", CancellationToken.None));
        var page = await repository.Query(new TelemetryQuery(Kind: TelemetryKind.Metric, Name: "cpu"), CancellationToken.None);
        Assert.Equal("cpu", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task DisplayQueriesRespectTimeBoundsAndApplicationWithoutShrinkingApplicationChoices()
    {
        var _repository = _mediator;
        var _from = new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
        var _to = _from.AddHours(3);
        foreach (var _service in new[] { "app-a", "app-b" })
        {
            foreach (var _timestamp in new[] { _from.AddMilliseconds(-1), _from, _to, _to.AddMilliseconds(1) })
            {
                await _repository.Store([
                    new() { Kind = TelemetryKind.Log, ServiceName = _service, TimestampUtc = _timestamp, Name = "log" },
                    new() { Kind = TelemetryKind.Trace, ServiceName = _service, TimestampUtc = _timestamp, Name = "trace", DurationMs = 10 },
                    new() { Kind = TelemetryKind.Request, ServiceName = _service, TimestampUtc = _timestamp, Name = "/test", Body = "GET", StatusCode = 200 },
                    new() { Kind = TelemetryKind.Metric, ServiceName = _service, TimestampUtc = _timestamp, Name = "aspnetcore.components.circuit.active", NumericValue = _timestamp > _to ? 999 : 3 }
                ], CancellationToken.None);
            }
        }

        var _summary = await _repository.GetSummary(_from, CancellationToken.None, _to, "app-a");
        Assert.Equal(2, _summary.Logs);
        Assert.Equal(2, _summary.Traces);
        Assert.Equal(2, _summary.Metrics);
        Assert.Equal("app-a", Assert.Single(_summary.Services));
        Assert.Equal(new[] { "app-a", "app-b" }, await _repository.GetServices(_from, _to, CancellationToken.None));
        var _page = await _repository.Query(new(ServiceName: "app-a", FromUtc: _from, ToUtc: _to), CancellationToken.None);
        Assert.Equal(8, _page.Total);
        Assert.All(_page.Items, _item => Assert.InRange(_item.TimestampUtc, _from, _to));
        var _breakdown = await _repository.GetDashboardBreakdown(_from, "app-a", null, CancellationToken.None, _to);
        Assert.Equal(2, _breakdown.HttpMethods["GET"]);
        Assert.Equal(2, _breakdown.HttpStatuses[200]);
        var _blazor = await _repository.GetBlazorDashboardMetrics(_from, "app-a", CancellationToken.None, _to);
        Assert.Equal(3, _blazor.ActiveCircuits);
        var _series = await _repository.GetMetricSeries("aspnetcore.components.circuit.active", "app-a", _from, CancellationToken.None, _to);
        Assert.Equal(2, _series.Count);
        Assert.All(_series, _point => Assert.Equal(3, _point.Value));
        var _empty = await _repository.GetSummary(_from, CancellationToken.None, _to, "missing-app");
        Assert.Equal(0, _empty.Logs);
        Assert.Empty(_empty.Services);
    }

    [Fact]
    public async Task ResourceMetricsSurviveBusyInstrumentsAndKeepRateBaselines()
    {
        _telemetryOptions.MaximumDashboardMetricRows = 128;
        var _repository = _mediator;
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        await _repository.Store([
            ProcessMetricSeriesTests.Item("process.cpu.time", _from.AddSeconds(-10), 100),
            ProcessMetricSeriesTests.Item("process.cpu.time", _from, 102),
            ProcessMetricSeriesTests.Item("process.cpu.time", _from.AddMinutes(2), 900),
            ProcessMetricSeriesTests.Item("process.memory.usage", _from, 1024 * 1024)
        ], CancellationToken.None);
        await _repository.Store(Enumerable.Range(0, 600).Select(_index => ProcessMetricSeriesTests.Item("busy.instrument", _from.AddSeconds(30), _index)).ToArray(), CancellationToken.None);
        var _items = await _repository.GetResourceMetrics(_from, _from.AddMinutes(1), "app", CancellationToken.None);
        Assert.Equal(3, _items.Count);
        Assert.DoesNotContain(_items, _item => _item.Name == "busy.instrument" || _item.TimestampUtc > _from.AddMinutes(1));
        Assert.Single(ProcessMetricSeries.Create(_items, _from, _from.AddMinutes(1)).Cpu);
        Assert.Empty(await _repository.GetResourceMetrics(_from, _from.AddMinutes(1), "other", CancellationToken.None));
    }

    [Fact]
    public async Task ResourceMetricsShareOneBudgetAndDoNotLoadUnrelatedPayloadsOrRetiredInstances()
    {
        _telemetryOptions.MaximumDashboardMetricRows = 128;
        var _repository = _mediator;
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        foreach (var _name in ResourceMetricNames.All)
        {
            var _items = Enumerable.Range(-100, 200).Select(_index =>
            {
                var _item = ProcessMetricSeriesTests.Item(_name, _from.AddSeconds(_index), 1000 + _index);
                _item.Body = new string('x', 4096);
                return _item;
            }).ToList();
            _items.AddRange(Enumerable.Range(0, 100).Select(_index => ProcessMetricSeriesTests.Item(
                _name, _from.AddSeconds(-1), 999, $"retired-{_index}")));
            await _repository.Store(_items, CancellationToken.None);
        }

        var _metrics = await _repository.GetResourceMetrics(_from, _from.AddSeconds(100), null, CancellationToken.None);
        Assert.InRange(_metrics.Count, 1, 128);
        Assert.Equal(ResourceMetricNames.All.Count, _metrics.Select(_item => _item.Name).Distinct().Count());
        Assert.All(_metrics, _item =>
        {
            Assert.Null(_item.Body);
            Assert.DoesNotContain("retired-", _item.ServiceInstanceId ?? string.Empty);
        });
        // Each counter retains the predecessor needed to calculate its first visible rate.
        foreach (var _name in ResourceMetricNames.All)
        {
            Assert.Equal(5, _metrics.Count(_item => _item.Name == _name));
        }
    }

    [Fact]
    public async Task EntityFrameworkBaselineKeepsDistantHistoryOnlyForActiveSeries()
    {
        _telemetryOptions.MaximumDashboardMetricRows = 128;
        var _repository = _mediator;
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        const string _name = "blazortelemetry.entity_framework.commands";
        const string _attributes = "{\"db.operation.type\":\"read\"}";
        await _repository.Store([
            ProcessMetricSeriesTests.Item(_name, _from.AddDays(-20), 1000, _attributes: _attributes),
            ProcessMetricSeriesTests.Item(_name, _from, 1005, _attributes: _attributes),
            ProcessMetricSeriesTests.Item(_name, _from.AddMinutes(1), 1010, _attributes: _attributes)
        ], CancellationToken.None);
        await _repository.Store(Enumerable.Range(0, 2000).Select(_index => ProcessMetricSeriesTests.Item(
            _name, _from.AddDays(-1), 5000, $"retired-{_index}", _attributes)).ToArray(), CancellationToken.None);

        var _breakdown = await _repository.GetDashboardBreakdown(_from, null, null, CancellationToken.None, _from.AddMinutes(1));
        Assert.Equal(10, _breakdown.EntityFrameworkOperations["read"]);
        var _empty = await _repository.GetDashboardBreakdown(_from.AddDays(1), null, null, CancellationToken.None, _from.AddDays(2));
        Assert.Empty(_empty.EntityFrameworkOperations);
    }

    [Fact]
    public async Task GetErrorCountsByServiceCountsEachApplicationWithinTheWindow()
    {
        var repository = _mediator;
        var now = DateTimeOffset.UtcNow;
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = now.AddSeconds(-10), ObservedUtc = now, ServiceName = "api", Name = "error-1", SeverityNumber = 17 },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = now.AddSeconds(-20), ObservedUtc = now, ServiceName = "api", Name = "error-2", SeverityNumber = 21 },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = now.AddSeconds(-30), ObservedUtc = now, ServiceName = "worker", Name = "error-3", SeverityNumber = 17 },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = now.AddSeconds(-40), ObservedUtc = now, ServiceName = "api", Name = "information", SeverityNumber = 9 },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = now.AddMinutes(-2), ObservedUtc = now, ServiceName = "api", Name = "old-error", SeverityNumber = 17 }
        ], CancellationToken.None);

        var counts = await repository.GetErrorCountsByService(now.AddMinutes(-1), null, null, CancellationToken.None);

        Assert.Equal(2, counts["api"]);
        Assert.Equal(1, counts["worker"]);
    }

    [Fact]
    public async Task DashboardBreakdownAggregatesRequestsAndEntityFrameworkIncrementsBeforePagination()
    {
        var repository = _mediator;
        var now = DateTimeOffset.UtcNow;
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = now.AddSeconds(-30), ObservedUtc = now, ServiceName = "api", Name = "/products", Body = "GET", StatusCode = 200 },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = now.AddSeconds(-20), ObservedUtc = now, ServiceName = "api", Name = "/products", Body = "POST", StatusCode = 201 },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = now.AddSeconds(-10), ObservedUtc = now, ServiceName = "BlazorTelemetry.Host", Name = "/telemetry", Body = "GET", StatusCode = 404 },
            new TelemetryItem { Kind = TelemetryKind.Metric, TimestampUtc = now.AddSeconds(-30), ObservedUtc = now, ServiceName = "api", Name = "blazortelemetry.entity_framework.commands", NumericValue = 4, ResourceAttributesJson = "{\"service.instance.id\":\"api-1\"}", AttributesJson = "{\"db.operation.type\":\"read\"}" },
            new TelemetryItem { Kind = TelemetryKind.Metric, TimestampUtc = now.AddSeconds(-10), ObservedUtc = now, ServiceName = "api", Name = "blazortelemetry.entity_framework.commands", NumericValue = 7, ResourceAttributesJson = "{\"service.instance.id\":\"api-1\"}", AttributesJson = "{\"db.operation.type\":\"read\"}" },
            new TelemetryItem { Kind = TelemetryKind.Metric, TimestampUtc = now.AddSeconds(-10), ObservedUtc = now, ServiceName = "api", Name = "blazortelemetry.entity_framework.commands", NumericValue = 2, ResourceAttributesJson = "{\"service.instance.id\":\"api-1\"}", AttributesJson = "{\"db.operation.type\":\"insert\"}" }
        ], CancellationToken.None);

        var breakdown = await repository.GetDashboardBreakdown(
            now.AddMinutes(-1),
            null,
            "BlazorTelemetry.Host",
            CancellationToken.None);

        Assert.Equal(1, breakdown.HttpMethods["GET"]);
        Assert.Equal(1, breakdown.HttpMethods["POST"]);
        Assert.Equal(1, breakdown.HttpStatuses[200]);
        Assert.Equal(1, breakdown.HttpStatuses[201]);
        Assert.DoesNotContain(404, breakdown.HttpStatuses.Keys);
        Assert.Equal(3, breakdown.EntityFrameworkOperations["read"]);
        Assert.Equal(0, breakdown.EntityFrameworkOperations["insert"]);
    }

    [Fact]
    public async Task BlazorDashboardMetricsAggregateCircuitCountersRatesAndHistogramPercentiles()
    {
        var repository = _mediator;
        var now = DateTimeOffset.UtcNow;
        await repository.Store([
            CreateMetric(now.AddSeconds(-30), "aspnetcore.components.circuit.active", 4, "sum", "{circuit}"),
            CreateMetric(now.AddSeconds(-10), "aspnetcore.components.circuit.active", 5, "sum", "{circuit}"),
            CreateMetric(now.AddSeconds(-30), "aspnetcore.components.circuit.connected", 2, "sum", "{circuit}"),
            CreateMetric(now.AddSeconds(-10), "aspnetcore.components.circuit.connected", 3, "sum", "{circuit}"),
            CreateMetric(now.AddSeconds(-30), "aspnetcore.components.navigation", 10, "sum", "{route}", "{\"aspnetcore.components.route\":\"/orders\",\"aspnetcore.components.type\":\"Orders\"}"),
            CreateMetric(now.AddSeconds(-10), "aspnetcore.components.navigation", 14, "sum", "{route}", "{\"aspnetcore.components.route\":\"/orders\",\"aspnetcore.components.type\":\"Orders\"}"),
            CreateMetric(now.AddSeconds(-10), "aspnetcore.components.navigation", 1, "sum", "{route}", "{\"aspnetcore.components.route\":\"/orders\",\"aspnetcore.components.type\":\"Orders\",\"error.type\":\"System.InvalidOperationException\"}"),
            CreateHistogram(now.AddSeconds(-30), "aspnetcore.components.event_handler", 0.3, 2, [0.1, 0.5, 1], [1, 1, 0, 0], "{\"aspnetcore.components.type\":\"OrderList\",\"aspnetcore.components.method\":\"Refresh\",\"aspnetcore.components.attribute.name\":\"onclick\"}"),
            CreateHistogram(now.AddSeconds(-10), "aspnetcore.components.event_handler", 1.5, 5, [0.1, 0.5, 1], [1, 3, 1, 0], "{\"aspnetcore.components.type\":\"OrderList\",\"aspnetcore.components.method\":\"Refresh\",\"aspnetcore.components.attribute.name\":\"onclick\"}"),
            CreateHistogram(now.AddSeconds(-10), "aspnetcore.components.update_parameters", 0.2, 2, [0.05, 0.1], [0, 1, 1], "{\"aspnetcore.components.type\":\"OrderList\"}"),
            CreateHistogram(now.AddSeconds(-10), "aspnetcore.components.render_diff", 0.08, 2, [0.02, 0.05], [1, 1, 0], "{\"aspnetcore.components.diff.length\":\"50\"}"),
            CreateHistogram(now.AddSeconds(-10), "aspnetcore.components.circuit.duration", 30, 2, [10, 20], [0, 1, 1], "{}")
        ], CancellationToken.None);

        var dashboard = await repository.GetBlazorDashboardMetrics(now.AddMinutes(-1), "web", CancellationToken.None);

        Assert.Equal(5, dashboard.ActiveCircuits);
        Assert.Equal(3, dashboard.ConnectedCircuits);
        Assert.Equal(5, dashboard.Navigations);
        Assert.Equal(1, dashboard.Errors);
        Assert.Equal(1_000, dashboard.EventHandlerP95Ms);
        Assert.Equal(100, dashboard.ComponentUpdateP95Ms);
        Assert.Equal(50, dashboard.RenderDiffP95Ms);
        Assert.Equal(20_000, dashboard.CircuitDurationP95Ms);
        var route = Assert.Single(dashboard.TopRoutes);
        Assert.Equal("/orders", route.Route);
        Assert.Equal(5, route.Navigations);
        Assert.Equal(1, route.Errors);
        var handler = Assert.Single(dashboard.SlowestEventHandlers);
        Assert.Equal("Refresh", handler.Method);
        Assert.Equal(3, handler.Invocations);
        Assert.Equal(400, handler.AverageDurationMs, 3);
        Assert.Equal(1_000, handler.P95DurationMs);
    }

    [Fact]
    public async Task BlazorDashboardMetricsKeepTheNewestRowsWithinTheMemoryLimit()
    {
        _telemetryOptions.MaximumDashboardMetricRows = 1;
        var repository = _mediator;
        var now = DateTimeOffset.UtcNow;
        await repository.Store([
            CreateMetric(now.AddSeconds(-30), "aspnetcore.components.circuit.active", 2, "gauge", "{circuit}"),
            CreateMetric(now.AddSeconds(-20), "aspnetcore.components.circuit.active", 3, "gauge", "{circuit}"),
            CreateMetric(now.AddSeconds(-10), "aspnetcore.components.circuit.active", 4, "gauge", "{circuit}")
        ], CancellationToken.None);

        var dashboard = await repository.GetBlazorDashboardMetrics(
            now.AddMinutes(-1),
            "web",
            CancellationToken.None);

        Assert.Equal(4, dashboard.ActiveCircuits);
        Assert.Single(dashboard.ActiveCircuitsSeries);
    }

}
