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

namespace BlazorTelemetry.Tests;

public sealed class TelemetryRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<TelemetryDbContext> _options;

    public TelemetryRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_databasePath}").Options;
    }

    public async Task InitializeAsync()
    {
        await using var context = new TelemetryDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task DisplayQueriesRespectTimeBoundsAndApplicationWithoutShrinkingApplicationChoices()
    {
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
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
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
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
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options), new BlazorTelemetryOptions { MaximumDashboardMetricRows = 128 });
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
            Assert.DoesNotContain("retired-", _item.ResourceAttributesJson);
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
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
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
    public async Task EntityFrameworkWindowUsesBaselineResetDeltaAndUpperBound()
    {
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        var _items = new[] { (-10, 1000d), (10, 1007d), (20, 3d), (30, 8d), (90, 10000d) }
            .Select(_point => ProcessMetricSeriesTests.Item("blazortelemetry.entity_framework.commands", _from.AddSeconds(_point.Item1), _point.Item2,
                _attributes: "{\"db.operation.type\":\"read\"}")).ToList();
        foreach (var _seconds in new[] { 10, 20 })
        {
            var _delta = ProcessMetricSeriesTests.Item("blazortelemetry.entity_framework.commands", _from.AddSeconds(_seconds), 4, "b", "{\"db.operation.type\":\"insert\"}");
            _delta.DetailsJson = "{\"data\":{\"aggregationTemporality\":\"Delta\"}}";
            _items.Add(_delta);
        }
        await _repository.Store(_items, CancellationToken.None);
        var _hour = await _repository.GetDashboardBreakdown(_from, "app", null, CancellationToken.None, _from.AddMinutes(1));
        Assert.Equal(15, _hour.EntityFrameworkOperations["read"]);
        Assert.Equal(8, _hour.EntityFrameworkOperations["insert"]);
        var _narrow = await _repository.GetDashboardBreakdown(_from.AddSeconds(15), "app", null, CancellationToken.None, _from.AddSeconds(25));
        Assert.Equal(3, _narrow.EntityFrameworkOperations["read"]);
        Assert.Equal(4, _narrow.EntityFrameworkOperations["insert"]);
    }

    [Fact]
    public async Task RequestCollectionTracksArbitraryRoutesAndCompletion()
    {
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var _context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        _context.Request.Path = "/customers/42/orders";
        _context.Request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");
        _context.Request.Scheme = "https";
        _context.Request.Method = "POST";
        _context.Request.Headers.UserAgent = "Request test agent";
        _context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var _middleware = new RequestTelemetryMiddleware(async _http =>
        {
            var _active = Assert.Single((await _repository.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
            Assert.Null(_active.DurationMs);
            _http.Response.StatusCode = 201;
        }, NullLogger<RequestTelemetryMiddleware>.Instance);

        await _middleware.InvokeAsync(_context, _repository, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ApplicationName = "test" });

        var _item = Assert.Single((await _repository.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
        Assert.Equal("https://localhost/customers/42/orders", _item.Name);
        Assert.Equal("POST", _item.Body);
        Assert.Equal(201, _item.StatusCode);
        Assert.True(_item.DurationMs >= 0);
        Assert.Contains("127.0.0.1", _item.AttributesJson);
        Assert.Contains("Request test agent", _item.AttributesJson);
    }

    [Fact]
    public async Task RequestCollectionUsesTheCurrentActivityForTraceCorrelation()
    {
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var _context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        _context.Request.Path = "/orders/42";
        using var _activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var _middleware = new RequestTelemetryMiddleware(_ => Task.CompletedTask, NullLogger<RequestTelemetryMiddleware>.Instance);

        await _middleware.InvokeAsync(_context, _repository,
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ApplicationName = "test" });

        var _item = Assert.Single((await _repository.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
        Assert.Equal(_activity.TraceId.ToHexString(), _item.TraceId);
        Assert.Equal(_activity.SpanId.ToHexString(), _item.SpanId);
    }

    [Fact]
    public async Task FailedRequestIsCompletedAndOriginalExceptionIsPreserved()
    {
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var _context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        _context.Request.Path = "/any-route.json";
        var _middleware = new RequestTelemetryMiddleware(_ => throw new InvalidOperationException("Test failure"), NullLogger<RequestTelemetryMiddleware>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _middleware.InvokeAsync(_context, _repository,
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ApplicationName = "test" }));
        var _item = Assert.Single((await _repository.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
        Assert.Equal(500, _item.StatusCode);
        Assert.NotNull(_item.DurationMs);
    }

    [Fact]
    public async Task CollectorRequestsDoNotRecordThemselves()
    {
        var _repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var _context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        _context.Request.Path = "/v1/traces";
        var _called = false;
        var _middleware = new RequestTelemetryMiddleware(_ => { _called = true; return Task.CompletedTask; }, NullLogger<RequestTelemetryMiddleware>.Instance);
        await _middleware.InvokeAsync(_context, _repository, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ApplicationName = "test" });
        Assert.True(_called);
        Assert.Empty((await _repository.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task QueryFiltersByKindServiceAndTrace()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = DateTimeOffset.UtcNow, ObservedUtc = DateTimeOffset.UtcNow, ServiceName = "api", Name = "ok", TraceId = "trace-1" },
            new TelemetryItem { Kind = TelemetryKind.Trace, TimestampUtc = DateTimeOffset.UtcNow, ObservedUtc = DateTimeOffset.UtcNow, ServiceName = "worker", Name = "job", TraceId = "trace-2", Fingerprint = "trace-2-span" }
        ], CancellationToken.None);

        var page = await repository.Query(new TelemetryQuery(TelemetryKind.Log, "api", TraceId: "trace-1"), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("ok", item.Name);
    }

    [Fact]
    public async Task QueryCanExcludeRequestsFromTheHostingApplicationBeforePagination()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var timestamp = DateTimeOffset.UtcNow;
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(2), ObservedUtc = timestamp, ServiceName = "BlazorTelemetry.Host", Name = "https://telemetry.example.com/" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(1), ObservedUtc = timestamp, ServiceName = "BlazorTelemetry.Host", Name = "http://localhost:8080/health/live" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "Customer.WebApp", Name = "https://customer.example.com/orders" }
        ], CancellationToken.None);

        var page = await repository.Query(new TelemetryQuery(
            TelemetryKind.Request,
            Take: 1,
            ExcludedServiceName: "BlazorTelemetry.Host"), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("Customer.WebApp", item.ServiceName);
        Assert.Equal(1, page.Total);
        Assert.False(page.IsTruncated);
    }

    [Fact]
    public async Task StoreDeduplicatesIdentifiableItemsAndKeepsLegitimateDuplicateLogs()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var timestamp = DateTimeOffset.UtcNow;

        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Trace, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "span", Fingerprint = "trace-span" },
            new TelemetryItem { Kind = TelemetryKind.Trace, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "span retried", Fingerprint = "trace-span" },
            new TelemetryItem { Kind = TelemetryKind.Trace, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "another span", Fingerprint = "trace-span-2" },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "same message" },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "same message" }
        ], CancellationToken.None);
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Trace, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "span retried later", Fingerprint = "trace-span" }
        ], CancellationToken.None);

        var traces = await repository.Query(new TelemetryQuery(TelemetryKind.Trace, Take: 20), CancellationToken.None);
        var logs = await repository.Query(new TelemetryQuery(TelemetryKind.Log, Take: 20), CancellationToken.None);

        Assert.Equal(2, traces.Total);
        Assert.Equal(2, logs.Total);
    }

    [Fact]
    public async Task QueryFiltersLogsByOtlpSeverityRangeBeforePagination()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var timestamp = DateTimeOffset.UtcNow;
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "debug", SeverityNumber = 5 },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp.AddSeconds(1), ObservedUtc = timestamp, ServiceName = "api", Name = "error", SeverityNumber = 17 },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp.AddSeconds(2), ObservedUtc = timestamp, ServiceName = "api", Name = "critical", SeverityNumber = 21 }
        ], CancellationToken.None);

        var page = await repository.Query(new TelemetryQuery(
            TelemetryKind.Log,
            Take: 1,
            MinimumSeverityNumber: 17,
            MaximumSeverityNumber: 20), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("error", item.Name);
        Assert.Equal(1, page.Total);
        Assert.False(page.IsTruncated);
    }

    [Fact]
    public async Task QueryFiltersRequestsByHttpStatusBeforePagination()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var timestamp = DateTimeOffset.UtcNow;
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "pending" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(1), ObservedUtc = timestamp, ServiceName = "api", Name = "switching", StatusCode = 101 },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(2), ObservedUtc = timestamp, ServiceName = "api", Name = "ok", StatusCode = 200 },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(3), ObservedUtc = timestamp, ServiceName = "api", Name = "not-found", StatusCode = 404 },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(4), ObservedUtc = timestamp, ServiceName = "api", Name = "error", StatusCode = 500 }
        ], CancellationToken.None);

        var successfulPage = await repository.Query(new TelemetryQuery(
            TelemetryKind.Request,
            Take: 1,
            MinimumStatusCode: 200,
            MaximumStatusCode: 299,
            HasStatusCode: true), CancellationToken.None);
        var inProgressPage = await repository.Query(new TelemetryQuery(
            TelemetryKind.Request,
            Take: 1,
            HasStatusCode: false), CancellationToken.None);

        Assert.Equal("ok", Assert.Single(successfulPage.Items).Name);
        Assert.Equal(1, successfulPage.Total);
        Assert.False(successfulPage.IsTruncated);
        Assert.Equal("pending", Assert.Single(inProgressPage.Items).Name);
        Assert.Equal(1, inProgressPage.Total);
        Assert.False(inProgressPage.IsTruncated);
    }

    [Fact]
    public async Task GetErrorCountsByServiceCountsEachApplicationWithinTheWindow()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
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
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
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
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
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
        var repository = new TelemetryRepository(
            new TestDbContextFactory(_options),
            new BlazorTelemetryOptions { MaximumDashboardMetricRows = 1 });
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

    [Fact]
    public async Task MetricIndexMigrationPreservesExistingDataAndSupportsTimeRangeQueries()
    {
        var _path = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-upgrade-{Guid.NewGuid():N}.db");
        var _options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_path}").Options;
        try
        {
            await using var _context = new TelemetryDbContext(_options);
            await _context.GetService<IMigrator>().MigrateAsync("20260916144728_InitialTelemetrySchema");
            _context.TelemetryItems.Add(CreateMetric(DateTimeOffset.UtcNow, "process.cpu.time", 10, "sum", "s"));
            await _context.SaveChangesAsync();

            await _context.Database.MigrateAsync();
            Assert.Equal(10, (await _context.TelemetryItems.AsNoTracking().SingleAsync()).NumericValue);
            await _context.Database.OpenConnectionAsync();
            using var _command = _context.Database.GetDbConnection().CreateCommand();
            _command.CommandText = "EXPLAIN QUERY PLAN SELECT Id FROM TelemetryItems WHERE Kind = 3 AND Name = 'process.cpu.time' AND TimestampUtc >= 0 ORDER BY TimestampUtc DESC LIMIT 100";
            await using var _reader = await _command.ExecuteReaderAsync();
            var _plan = new List<string>();
            while (await _reader.ReadAsync())
            {
                _plan.Add(_reader.GetString(3));
            }
            Assert.Contains(_plan, _line => _line.Contains("IX_TelemetryItems_Kind_Name_TimestampUtc", StringComparison.Ordinal));
            Assert.DoesNotContain(_plan, _line => _line.Contains("TEMP B-TREE", StringComparison.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task DatabaseInitializerCreatesDefaultPerApplicationErrorRule()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-default-rule-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TelemetryDbContext>>(new TestDbContextFactory(options))
            .BuildServiceProvider();

        try
        {
            var initializer = new TelemetryDatabaseInitializer(services, NullLogger<TelemetryDatabaseInitializer>.Instance);
            await initializer.StartAsync(CancellationToken.None);

            await using var context = new TelemetryDbContext(options);
            var rule = await context.AlertRules.SingleAsync();
            Assert.Equal("More than 5 errors in one minute", rule.Name);
            Assert.Equal("*", rule.ServiceName);
            Assert.Equal(AlertRuleType.ErrorLogs, rule.Type);
            Assert.Equal(5, rule.Threshold);
            Assert.Equal(1, rule.WindowMinutes);
            Assert.Equal(0, rule.ConfirmationMinutes);
            Assert.True(rule.IsEnabled);
        }
        finally
        {
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task AlertEvaluationFiresOnlyForApplicationWithMoreThanFiveErrors()
    {
        var factory = new TestDbContextFactory(_options);
        var repository = new TelemetryRepository(factory);
        var rule = new AlertRule
        {
            Name = "Application error burst",
            ServiceName = "*",
            Type = AlertRuleType.ErrorLogs,
            Threshold = 5,
            WindowMinutes = 1,
            ConfirmationMinutes = 0
        };
        await repository.SaveAlertRule(rule, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var apiErrors = Enumerable.Range(0, 6).Select(index => new TelemetryItem
        {
            Kind = TelemetryKind.Log,
            TimestampUtc = now.AddSeconds(-index),
            ObservedUtc = now,
            ServiceName = "api",
            Name = $"api-error-{index}",
            SeverityNumber = 17
        });
        var workerErrors = Enumerable.Range(0, 5).Select(index => new TelemetryItem
        {
            Kind = TelemetryKind.Log,
            TimestampUtc = now.AddSeconds(-index),
            ObservedUtc = now,
            ServiceName = "worker",
            Name = $"worker-error-{index}",
            SeverityNumber = 17
        });
        await repository.Store(apiErrors.Concat(workerErrors).ToArray(), CancellationToken.None);

        await using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TelemetryDbContext>>(factory)
            .AddSingleton<ITelemetryRepository>(repository)
            .BuildServiceProvider();
        var evaluationService = new AlertEvaluationService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new BlazorTelemetryOptions(),
            NullLogger<AlertEvaluationService>.Instance);

        await evaluationService.Evaluate(CancellationToken.None);

        await using var context = new TelemetryDbContext(_options);
        var incident = await context.Incidents.SingleAsync();
        Assert.Equal("api", incident.ServiceName);
        Assert.Equal(6, incident.ObservedValue);
        Assert.Equal(IncidentState.Firing, incident.State);
    }

    [Fact]
    public async Task PurgeEnforcesLogicalTelemetryBudget()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var timestamp = DateTimeOffset.UtcNow;
        await repository.Store(Enumerable.Range(0, 20).Select(index => new TelemetryItem
        {
            Kind = TelemetryKind.Log,
            TimestampUtc = timestamp.AddSeconds(index),
            ObservedUtc = timestamp,
            ServiceName = "budget",
            Name = $"log-{index}",
            Body = new string('x', 512)
        }).ToArray(), CancellationToken.None);

        var purged = await repository.Purge(timestamp.AddDays(-1), timestamp.AddDays(-1), 1, CancellationToken.None);
        var remaining = await repository.Query(new TelemetryQuery(Take: 100), CancellationToken.None);

        Assert.Equal(20, purged);
        Assert.Empty(remaining.Items);
    }

    [Fact]
    public async Task RevokedIngestionApplicationCannotBeResolved()
    {
        var repository = new TelemetryRepository(new TestDbContextFactory(_options));
        var application = new IngestionApplication { Name = "api", KeyHash = "HASH", KeyPrefix = "bt_test" };
        await repository.SaveIngestionApplication(application, CancellationToken.None);

        Assert.NotNull(await repository.FindActiveIngestionApplication("HASH", CancellationToken.None));
        application.IsActive = false;
        await repository.SaveIngestionApplication(application, CancellationToken.None);

        Assert.Null(await repository.FindActiveIngestionApplication("HASH", CancellationToken.None));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
        return Task.CompletedTask;
    }

    private static TelemetryItem CreateMetric(
        DateTimeOffset timestamp,
        string name,
        double value,
        string type,
        string unit,
        string attributes = "{}")
    {
        return new TelemetryItem
        {
            Kind = TelemetryKind.Metric,
            TimestampUtc = timestamp,
            ObservedUtc = timestamp,
            ServiceName = "web",
            Name = name,
            NumericValue = value,
            MetricType = type,
            Unit = unit,
            ResourceAttributesJson = "{\"service.instance.id\":\"web-1\"}",
            AttributesJson = attributes,
            DetailsJson = "{}"
        };
    }

    private static TelemetryItem CreateHistogram(
        DateTimeOffset timestamp,
        string name,
        double sum,
        long count,
        IReadOnlyList<double> bounds,
        IReadOnlyList<long> buckets,
        string attributes)
    {
        var details = System.Text.Json.JsonSerializer.Serialize(new
        {
            scope = "Microsoft.AspNetCore.Components",
            data = new { count, sum, min = 0d, max = bounds.LastOrDefault(), bounds, buckets }
        });
        var item = CreateMetric(timestamp, name, sum, "histogram", "s", attributes);
        item.DetailsJson = details;
        return item;
    }

    private sealed class TestDbContextFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
    {
        public TelemetryDbContext CreateDbContext() => new(options);
    }
}
