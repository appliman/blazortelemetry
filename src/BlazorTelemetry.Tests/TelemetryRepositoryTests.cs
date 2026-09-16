using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

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

    private sealed class TestDbContextFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
    {
        public TelemetryDbContext CreateDbContext() => new(options);
    }
}
