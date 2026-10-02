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
    public async Task TypedMigrationDropsLegacyTelemetryAndSupportsTimeRangeQueries()
    {
        var _path = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-upgrade-{Guid.NewGuid():N}.db");
        var _options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_path}").Options;
        try
        {
            await using var _context = new TelemetryDbContext(_options);
            await _context.GetService<IMigrator>().MigrateAsync("20260916144728_InitialTelemetrySchema");
            await _context.Database.ExecuteSqlRawAsync("INSERT INTO TelemetryItems (Kind,TimestampUtc,ObservedUtc,ServiceName,Name,ResourceAttributesJson,AttributesJson,DetailsJson) VALUES (3,0,0,'web','legacy','{{}}','{{}}','{{}}')");
            await _context.Database.MigrateAsync();
            Assert.Empty(await _context.TelemetryItems.ToListAsync());
            await _context.StoreTelemetry([CreateMetric(DateTimeOffset.UtcNow, "process.cpu.time", 10, "sum", "s")], CancellationToken.None);
            Assert.Equal(10, (await _context.TelemetryItems.SingleAsync()).NumericValue);
            await _context.Database.OpenConnectionAsync();
            using var _command = _context.Database.GetDbConnection().CreateCommand();
            _command.CommandText = "EXPLAIN QUERY PLAN SELECT Id FROM TelemetryMetrics WHERE Name = 'process.cpu.time' AND TimestampUtc >= 0 ORDER BY TimestampUtc DESC LIMIT 100";
            await using var _reader = await _command.ExecuteReaderAsync();
            var _plan = new List<string>();
            while (await _reader.ReadAsync())
            {
                _plan.Add(_reader.GetString(3));
            }
            Assert.Contains(_plan, _line => _line.Contains("IX_TelemetryMetrics_Name_TimestampUtc", StringComparison.Ordinal));
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
        var initializationServices = new ServiceCollection();
        initializationServices.AddLogging();
        initializationServices.AddSingleton<IDbContextFactory<TelemetryDbContext>>(new TestDbContextFactory(options));
        initializationServices.AddSingleton(new BlazorTelemetryOptions());
        initializationServices.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(BlazorTelemetryServiceCollectionExtensions).Assembly);
        var services = initializationServices.BuildServiceProvider();

        try
        {
            var initialization = await services.GetRequiredService<IMediator>().Send(new InitializeTelemetryDatabaseRequest(), CancellationToken.None);
            Assert.False(initialization.HasError);

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
    public async Task PurgeEnforcesLogicalTelemetryBudget()
    {
        var repository = _mediator;
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

}
