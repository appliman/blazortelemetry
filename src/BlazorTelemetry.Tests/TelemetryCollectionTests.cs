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
    public async Task EntityFrameworkWindowUsesBaselineResetDeltaAndUpperBound()
    {
        _telemetryOptions.MaximumDashboardMetricRows = 128;
        var _repository = _mediator;
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        var _items = new[] { (-10, 1000d), (10, 1007d), (20, 3d), (30, 8d), (90, 10000d) }
            .Select(_point => ProcessMetricSeriesTests.Item("blazortelemetry.entity_framework.commands", _from.AddSeconds(_point.Item1), _point.Item2,
                _attributes: "{\"db.operation.type\":\"read\"}")).ToList();
        foreach (var _seconds in new[] { 10, 20 })
        {
            var _delta = ProcessMetricSeriesTests.Item("blazortelemetry.entity_framework.commands", _from.AddSeconds(_seconds), 4, "b", "{\"db.operation.type\":\"insert\"}");
            _delta.AggregationTemporality = "Delta";
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
        var _repository = _mediator;
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
        Assert.Equal("127.0.0.1", _item.ClientAddress);
        Assert.DoesNotContain("client.address", _item.AttributesJson);
        Assert.Equal("Request test agent", _item.UserAgent);
        Assert.DoesNotContain("user_agent.original", _item.AttributesJson);
    }

    [Fact]
    public async Task RequestCollectionUsesTheCurrentActivityForTraceCorrelation()
    {
        var _repository = _mediator;
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
        var _repository = _mediator;
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
        var _repository = _mediator;
        var _context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        _context.Request.Path = "/v1/traces";
        var _called = false;
        var _middleware = new RequestTelemetryMiddleware(_ => { _called = true; return Task.CompletedTask; }, NullLogger<RequestTelemetryMiddleware>.Instance);
        await _middleware.InvokeAsync(_context, _repository, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ApplicationName = "test" });
        Assert.True(_called);
        Assert.Empty((await _repository.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
    }

}
