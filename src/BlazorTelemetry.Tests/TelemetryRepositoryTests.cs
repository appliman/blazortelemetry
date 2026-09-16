using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private sealed class TestDbContextFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
    {
        public TelemetryDbContext CreateDbContext() => new(options);
    }
}
