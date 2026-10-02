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
using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

namespace BlazorTelemetry.Tests;

public sealed partial class TelemetryCqrsTests
{
    [Fact]
    public async Task ImportPurchaseDuplicateProductErrorSurvivesProtobufAndSqliteRoundTrip()
    {
        var _timestamp = DateTimeOffset.Parse("2026-09-30T11:00:26.939Z");
        var _requestBody = """
            {"purchase":{"code":"60014778","supplierCode":"F0024","supplierName":"FAUVI BROD","companyName":"Rousseau Quincaillerie","isBalanced":true,"creationDate":"2026-09-10T08:12:11Z","items":[{"productCode":"CREASERICD101A501","supplierProductCode":"","quantity":"2","unitPurchasePriceWithoutTax":"2.95","productDesignation":"Sérigraphie Coeur et Dos de 101 à 501 pièces","barCode":""},{"productCode":"CREASERICD101A501","supplierProductCode":"","quantity":"2","unitPurchasePriceWithoutTax":"2.95","productDesignation":"Sérigraphie Coeur et Dos de 101 à 501 pièces","barCode":""},{"productCode":"CREASERICD101A501","supplierProductCode":"","quantity":"2","unitPurchasePriceWithoutTax":"2.95","productDesignation":"Sérigraphie Coeur et Dos de 101 à 501 pièces","barCode":""}]}}
            """;
        var _responseBody = """
            {"status":3,"errors":{"exception":"An item with the same key has already been added. Key: CREASERICD101A501"},"warnings":[]}
            """;
        var _stackTrace = """
            System.ArgumentException: An item with the same key has already been added. Key: CREASERICD101A501
               at System.Collections.Generic.Dictionary`2.TryInsert(TKey key, TValue value, InsertionBehavior behavior)
               at AuditStock.Handlers.Imports.ImportPurchaseRequestHandler.Handle(ImportPurchaseRequest request, CancellationToken cancellationToken) in /build/src/AuditStock.Core/Handlers/Imports/ImportPurchaseRequestHandler.cs:line 128
            """;
        var _body = $"API request POST /api/v15/imports/import-purchase from 78.24.32.55 returned 500 in 142ms. Content-Type: application/json; charset=utf-8. Request: {_requestBody}. Response: {_responseBody}";
        var _record = new OpenTelemetry.Proto.Logs.V1.LogRecord
        {
            TimeUnixNano = (ulong)_timestamp.ToUnixTimeMilliseconds() * 1_000_000,
            SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber.Error,
            SeverityText = "Error",
            Body = new OpenTelemetry.Proto.Common.V1.AnyValue { StringValue = _body }
        };
        _record.Attributes.Add(new OpenTelemetry.Proto.Common.V1.KeyValue
        {
            Key = "exception.stacktrace",
            Value = new OpenTelemetry.Proto.Common.V1.AnyValue { StringValue = _stackTrace }
        });
        var _scope = new OpenTelemetry.Proto.Logs.V1.ScopeLogs
        {
            Scope = new OpenTelemetry.Proto.Common.V1.InstrumentationScope { Name = "AuditStock.AdminWebApp.Middlewares.ApiRequestLogMiddleware" }
        };
        _scope.LogRecords.Add(_record);
        var _resource = new OpenTelemetry.Proto.Logs.V1.ResourceLogs();
        _resource.ScopeLogs.Add(_scope);
        var _payload = new OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest();
        _payload.ResourceLogs.Add(_resource);
        using var _stream = new MemoryStream();
        using (var _output = new Google.Protobuf.CodedOutputStream(_stream, leaveOpen: true))
        {
            _payload.WriteTo(_output);
        }
        _stream.Position = 0;
        var _parsedPayload = OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest.Parser.ParseFrom(_stream);
        var _parser = new OtlpParser(new OtlpValueConverter(_telemetryOptions));
        var _items = _parser.ParseLogs(_parsedPayload, "proxiwebpro");
        var _result = await _mediator.Send(new StoreTelemetryBatchRequest(_items), CancellationToken.None);
        Assert.False(_result.HasError);
        Assert.Equal(1, _result.ChangeCount);

        await using var _context = new TelemetryDbContext(_options);
        var _stored = Assert.Single(await _context.TelemetryItems.AsNoTracking().ToListAsync());
        Assert.Equal(_body, _stored.Body);
        Assert.Equal(_timestamp, _stored.TimestampUtc);
        Assert.Equal(17, _stored.SeverityNumber);
        Assert.Equal("proxiwebpro", _stored.ServiceName);
        using var _attributes = System.Text.Json.JsonDocument.Parse(_stored.AttributesJson);
        Assert.Equal(_stackTrace, _attributes.RootElement.GetProperty("exception.stacktrace").GetString());
        var _page = await _mediator.Query(new TelemetryQuery(TelemetryKind.Log, "proxiwebpro",
            Search: "CREASERICD101A501", MinimumSeverityNumber: 17,
            FromUtc: _timestamp.AddMinutes(-1), ToUtc: _timestamp.AddMinutes(1)), CancellationToken.None);
        Assert.Equal(_stored.Id, Assert.Single(_page.Items).Id);
    }

    [Fact]
    public async Task QueryFailureReturnsBrokenRulesInsteadOfAnEmptyPage()
    {
        var missingDatabase = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-missing-{Guid.NewGuid():N}.db");
        var invalidOptions = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={missingDatabase}").Options;
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IDbContextFactory<TelemetryDbContext>>(new TestDbContextFactory(invalidOptions));
        registrations.AddSingleton(new BlazorTelemetryOptions());
        registrations.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(BlazorTelemetryServiceCollectionExtensions).Assembly);
        await using var provider = registrations.BuildServiceProvider();

        var result = await provider.GetRequiredService<IMediator>().Send(new QueryTelemetryRequest(new TelemetryQuery()), CancellationToken.None);
        Assert.True(result.HasError);
        Assert.Null(result.Data);
        Assert.Equal("storage_error", result.BrokenRules[0].Code);
        SqliteConnection.ClearAllPools();
        if (File.Exists(missingDatabase))
        {
            File.Delete(missingDatabase);
        }
    }

    [Fact]
    public async Task QueryFiltersByKindServiceAndTrace()
    {
        var repository = _mediator;
        await repository.Store([
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = DateTimeOffset.UtcNow, ObservedUtc = DateTimeOffset.UtcNow, ServiceName = "api", Name = "ok", TraceId = "trace-1" },
            new TelemetryItem { Kind = TelemetryKind.Trace, TimestampUtc = DateTimeOffset.UtcNow, ObservedUtc = DateTimeOffset.UtcNow, ServiceName = "worker", Name = "job", TraceId = "trace-2", Fingerprint = "trace-2-span" }
        ], CancellationToken.None);

        var page = await repository.Query(new TelemetryQuery(TelemetryKind.Log, "api", TraceId: "trace-1"), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("ok", item.Name);
    }

    [Fact]
    public async Task QueryHonorsConfiguredMaximumRows()
    {
        _telemetryOptions.MaximumQueryRows = 1;
        var timestamp = DateTimeOffset.UtcNow;
        await _mediator.Store([
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "first" },
            new TelemetryItem { Kind = TelemetryKind.Log, TimestampUtc = timestamp.AddSeconds(1), ObservedUtc = timestamp, ServiceName = "api", Name = "second" }
        ], CancellationToken.None);

        var page = await _mediator.Query(new TelemetryQuery(TelemetryKind.Log, Take: 10), CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(2, page.Total);
        Assert.True(page.IsTruncated);
    }

    [Fact]
    public async Task QueryCanExcludeRequestsFromTheHostingApplicationBeforePagination()
    {
        var repository = _mediator;
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
        var repository = _mediator;
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
    public async Task ConcurrentBatchesArePersistedWithoutSqliteWriteContention()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(index =>
            _mediator.Send(new StoreTelemetryBatchRequest(
                Enumerable.Range(0, 5).Select(item => new TelemetryItem
                {
                    Kind = TelemetryKind.Log,
                    TimestampUtc = timestamp,
                    ObservedUtc = timestamp,
                    ServiceName = "concurrent-api",
                    Name = $"batch-{index}-item-{item}"
                }).ToArray()), CancellationToken.None)));

        Assert.All(results, result => Assert.False(result.HasError));
        var page = await _mediator.Query(new TelemetryQuery(TelemetryKind.Log, "concurrent-api", Take: 150), CancellationToken.None);
        Assert.Equal(120, page.Total);
    }

    [Fact]
    public async Task QueryFiltersLogsByOtlpSeverityRangeBeforePagination()
    {
        var repository = _mediator;
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
        var repository = _mediator;
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
    public async Task QueryRequiresRequestMethodAndIpBeforeCountingAndPagination()
    {
        var timestamp = DateTimeOffset.UtcNow;
        await _mediator.Store([
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(8), ObservedUtc = timestamp, ServiceName = "api", Name = "placeholder-ip", Body = "GET", AttributesJson = "{\"client.address\":\"—\"}" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(7), ObservedUtc = timestamp, ServiceName = "api", Name = "invalid-attributes", Body = "GET", AttributesJson = "invalid json" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(6), ObservedUtc = timestamp, ServiceName = "api", Name = "missing-method", AttributesJson = "{\"client.address\":\"203.0.113.1\"}" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(5), ObservedUtc = timestamp, ServiceName = "api", Name = "missing-ip", Body = "GET" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(4), ObservedUtc = timestamp, ServiceName = "api", Name = "placeholder-method", Body = "—", AttributesJson = "{\"client.address\":\"203.0.113.1\"}" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(3), ObservedUtc = timestamp, ServiceName = "api", Name = "navigation", Body = "Blazor navigation", AttributesJson = "{\"client.address\":\"203.0.113.1\"}" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(2), ObservedUtc = timestamp, ServiceName = "api", Name = "empty-ip", Body = "POST", AttributesJson = "{\"client.address\":\"  \"}" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp.AddSeconds(1), ObservedUtc = timestamp, ServiceName = "api", Name = "fallback-ip", Body = "POST", AttributesJson = "{\"client.address\":\"  \",\"network.peer.address\":\"203.0.113.2\"}" },
            new TelemetryItem { Kind = TelemetryKind.Request, TimestampUtc = timestamp, ObservedUtc = timestamp, ServiceName = "api", Name = "primary-ip", Body = "GET", AttributesJson = "{\"client.address\":\"203.0.113.1\"}" }
        ], CancellationToken.None);

        var page = await _mediator.Query(new TelemetryQuery(TelemetryKind.Request, Take: 1, RequireHttpRequestDetails: true), CancellationToken.None);
        var unfiltered = await _mediator.Query(new TelemetryQuery(TelemetryKind.Request, Take: 10), CancellationToken.None);

        Assert.Equal("fallback-ip", Assert.Single(page.Items).Name);
        Assert.Equal(2, page.Total);
        Assert.True(page.IsTruncated);
        Assert.Equal(9, unfiltered.Total);
    }

}
