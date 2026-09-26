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

}
