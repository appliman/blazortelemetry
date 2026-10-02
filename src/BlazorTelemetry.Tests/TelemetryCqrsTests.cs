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

public sealed partial class TelemetryCqrsTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<TelemetryDbContext> _options;
    private readonly BlazorTelemetryOptions _telemetryOptions = new();
    private ServiceProvider _serviceProvider = null!;
    private IMediator _mediator = null!;

    public TelemetryCqrsTests()
    {
        _options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_databasePath}").Options;
    }

    public async Task InitializeAsync()
    {
        await using var context = new TelemetryDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<TelemetryDbContext>>(new TestDbContextFactory(_options));
        services.AddSingleton(_telemetryOptions);
        services.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(BlazorTelemetryServiceCollectionExtensions).Assembly);
        services.AddValidatorsFromAssemblyContaining<AlertRuleValidator>();
        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
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
        var item = CreateMetric(timestamp, name, sum, "histogram", "s", attributes);
        item.ScopeName = "Microsoft.AspNetCore.Components";
        item.Count = count;
        item.Sum = sum;
        item.Minimum = 0;
        item.Maximum = bounds.LastOrDefault();
        item.Buckets = buckets.Select((_count, _ordinal) => new TelemetryMetricBucket
        {
            Ordinal = _ordinal,
            Count = _count,
            UpperBound = _ordinal < bounds.Count ? bounds[_ordinal] : null
        }).ToList();
        return item;
    }

}
