using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;
using BlazorTelemetry.Host.ModelExtensions;
using BlazorTelemetry.Sqlite;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;

namespace BlazorTelemetry.Tests;

public sealed partial class TelemetryCqrsTests
{
    [Fact]
    public async Task MixedBatchUsesDedicatedSchemasAndOneCanonicalResource()
    {
        var _now = DateTimeOffset.UtcNow;
        var _items = Enum.GetValues<TelemetryKind>().Select(_kind => new TelemetryItem
        {
            Kind = _kind, TimestampUtc = _now, ObservedUtc = _now, ServiceName = "api", Name = _kind.ToString(),
            ResourceAttributesJson = "{\"service.instance.id\":\"one\",\"telemetry.sdk.name\":\"opentelemetry\",\"custom\":{\"z\":1,\"a\":2}}"
        }).ToArray();
        _items[1].ResourceAttributesJson = "{\"custom\":{\"a\":2,\"z\":1},\"telemetry.sdk.name\":\"opentelemetry\",\"service.instance.id\":\"one\"}";
        await _mediator.Store(_items, CancellationToken.None);
        await using var _context = new TelemetryDbContext(_options);
        Assert.Single(await _context.TelemetryLogs.ToListAsync());
        Assert.Single(await _context.TelemetryTraces.ToListAsync());
        Assert.Single(await _context.TelemetryMetrics.ToListAsync());
        Assert.Single(await _context.TelemetryRequest.ToListAsync());
        var _resource = Assert.Single(await _context.TelemetryResources.ToListAsync());
        Assert.Equal("one", _resource.ServiceInstanceId);
        Assert.Equal("opentelemetry", _resource.SdkName);
        Assert.DoesNotContain("service.instance.id", _resource.AttributesJson);
        Assert.Contains("custom", _resource.AttributesJson);
        Assert.Null(_context.Model.FindEntityType(typeof(TelemetryItem)));
        Assert.Null(_context.Model.FindEntityType(typeof(TelemetryMetric))!.FindProperty("SeverityNumber"));
        Assert.Null(_context.Model.FindEntityType(typeof(TelemetryLog))!.FindProperty("NumericValue"));
        Assert.All(_context.Model.GetEntityTypes(), _entity => Assert.Null(_entity.FindProperty("DetailsJson")));
    }

    [Fact]
    public async Task ResourceHashCollisionDoesNotMergeDifferentContents()
    {
        var _item = new TelemetryItem { Kind = TelemetryKind.Log, ServiceName = "target", Name = "log" };
        var _resource = TelemetryNormalization.Resource(_item);
        _resource.ServiceName = "different";
        await using (var _context = new TelemetryDbContext(_options))
        {
            _context.TelemetryResources.Add(_resource);
            await _context.SaveChangesAsync();
        }
        await _mediator.Store([_item], CancellationToken.None);
        await using var _verification = new TelemetryDbContext(_options);
        Assert.Equal(2, await _verification.TelemetryResources.CountAsync());
        Assert.Equal("target", (await _verification.TelemetryLogs.Include(_log => _log.Resource).SingleAsync()).Resource.ServiceName);
    }

    [Fact]
    public async Task HttpColumnsPreserveConflictingAliasesAndUnconvertibleValues()
    {
        var _item = new TelemetryItem
        {
            Kind = TelemetryKind.Request, ServiceName = "api", Name = "/orders", Body = "GET",
            AttributesJson = "{\"client.address\":\"203.0.113.1\",\"network.peer.address\":\"203.0.113.2\",\"http.request.method\":\"GET\",\"user_agent.original\":\"agent\",\"client.port\":\"invalid\",\"server.port\":443,\"custom\":true}"
        };
        await _mediator.Store([_item], CancellationToken.None);
        var _stored = Assert.Single((await _mediator.Query(new TelemetryQuery(TelemetryKind.Request), CancellationToken.None)).Items);
        Assert.Equal("203.0.113.1", _stored.ClientAddress);
        Assert.Equal("GET", _stored.HttpMethod);
        Assert.Equal("agent", _stored.UserAgent);
        Assert.Equal(443, _stored.ServerPort);
        Assert.Null(_stored.ClientPort);
        using var _attributes = JsonDocument.Parse(_stored.AttributesJson);
        Assert.False(_attributes.RootElement.TryGetProperty("client.address", out _));
        Assert.False(_attributes.RootElement.TryGetProperty("http.request.method", out _));
        Assert.False(_attributes.RootElement.TryGetProperty("server.port", out _));
        Assert.Equal("203.0.113.2", _attributes.RootElement.GetProperty("network.peer.address").GetString());
        Assert.Equal("invalid", _attributes.RootElement.GetProperty("client.port").GetString());
        Assert.True(_attributes.RootElement.GetProperty("custom").GetBoolean());
        Assert.Single((await _mediator.Query(new TelemetryQuery(Search: "203.0.113.1"), CancellationToken.None)).Items);
        Assert.Single((await _mediator.Query(new TelemetryQuery(Search: "agent"), CancellationToken.None)).Items);
        Assert.Single((await _mediator.Query(new TelemetryQuery(Search: "443"), CancellationToken.None)).Items);
        Assert.Single((await _mediator.Query(new TelemetryQuery(Search: "44"), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task GlobalPaginationUsesKindAndIdAndDeduplicationIsPerCategory()
    {
        var _now = DateTimeOffset.Parse("2026-10-01T10:00:00+02:00");
        var _items = new[] { TelemetryKind.Trace, TelemetryKind.Metric, TelemetryKind.Request }.Select(_kind => new TelemetryItem
        {
            Kind = _kind, TimestampUtc = _now, ObservedUtc = _now, ServiceName = "api", Name = _kind.ToString(), Fingerprint = "shared"
        }).ToArray();
        await _mediator.Store(_items.Concat(_items).ToArray(), CancellationToken.None);
        var _first = await _mediator.Query(new TelemetryQuery(Take: 2), CancellationToken.None);
        var _second = await _mediator.Query(new TelemetryQuery(Take: 2, Skip: 2), CancellationToken.None);
        Assert.Equal(3, _first.Total);
        Assert.True(_first.IsTruncated);
        Assert.False(_second.IsTruncated);
        Assert.Equal(new[] { TelemetryKind.Trace, TelemetryKind.Metric, TelemetryKind.Request }, _first.Items.Concat(_second.Items).Select(_item => _item.Kind));
        Assert.All(_first.Items.Concat(_second.Items), _item => Assert.Equal(TimeSpan.Zero, _item.TimestampUtc.Offset));
        Assert.All(_first.Items.Concat(_second.Items), _item => Assert.Equal(1, _item.Id));
        Assert.DoesNotContain("DetailsJson", JsonSerializer.Serialize(_first));
    }

    [Fact]
    public async Task ChildCollectionsRoundTripInOrderAndCascadeDuringPurge()
    {
        var _now = DateTimeOffset.UtcNow;
        var _trace = new TelemetryItem
        {
            Kind = TelemetryKind.Trace, Name = "span", ServiceName = "api", TimestampUtc = _now, ObservedUtc = _now,
            Events = [new() { Name = "first", TimestampUtc = _now, AttributesJson = "{\"dynamic\":1}" }, new() { Name = "second", TimestampUtc = _now }],
            Links = [new() { TraceId = "linked", SpanId = "span", AttributesJson = "{\"relation\":\"test\"}" }]
        };
        var _metric = CreateHistogram(_now, "duration", 2, 3, [1, 2], [1, 1, 1], "{}");
        _metric.Quantiles = [new() { Quantile = 0.5, Value = 1 }];
        _metric.Exemplars = [new() { TimestampUtc = _now, TraceId = "linked", SpanId = "span", Value = 1, AttributesJson = "{\"label\":\"sample\"}" }];
        await _mediator.Store([_trace, _metric], CancellationToken.None);
        var _page = await _mediator.Query(new TelemetryQuery(), CancellationToken.None);
        var _storedTrace = Assert.Single(_page.Items, _item => _item.Kind == TelemetryKind.Trace);
        Assert.Equal(new[] { "first", "second" }, _storedTrace.Events.Select(_event => _event.Name));
        Assert.Equal("linked", Assert.Single(_storedTrace.Links).TraceId);
        var _storedMetric = Assert.Single(_page.Items, _item => _item.Kind == TelemetryKind.Metric);
        Assert.Equal(new long[] { 1, 1, 1 }, _storedMetric.Buckets.Select(_bucket => _bucket.Count));
        Assert.Null(_storedMetric.Buckets[^1].UpperBound);
        Assert.Equal(0.5, Assert.Single(_storedMetric.Quantiles).Quantile);
        Assert.Equal("sample", JsonDocument.Parse(Assert.Single(_storedMetric.Exemplars).AttributesJson).RootElement.GetProperty("label").GetString());
        Assert.Equal(2, await _mediator.Purge(_now.AddSeconds(1), _now.AddSeconds(1), long.MaxValue, CancellationToken.None));
        await using var _context = new TelemetryDbContext(_options);
        Assert.Equal(0, await _context.Set<TelemetryTraceEvent>().CountAsync());
        Assert.Equal(0, await _context.Set<TelemetryTraceLink>().CountAsync());
        Assert.Equal(0, await _context.Set<TelemetryMetricBucket>().CountAsync());
        Assert.Equal(0, await _context.Set<TelemetryMetricQuantile>().CountAsync());
        Assert.Equal(0, await _context.Set<TelemetryMetricExemplar>().CountAsync());
        Assert.Equal(0, await _context.TelemetryResources.CountAsync());
    }

    [Fact]
    public async Task FailedMixedBatchRollsBackRecordsAndResources()
    {
        var _result = await _mediator.Send(new StoreTelemetryBatchRequest([
            new() { Kind = TelemetryKind.Log, Name = "valid", ServiceName = "api" },
            new() { Kind = (TelemetryKind)999, Name = "invalid", ServiceName = "api" }
        ]), CancellationToken.None);
        Assert.True(_result.HasError);
        await using var _context = new TelemetryDbContext(_options);
        Assert.Equal(0, await _context.TelemetryLogs.CountAsync());
        Assert.Equal(0, await _context.TelemetryResources.CountAsync());
        using var _cancelled = new CancellationTokenSource();
        _cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _mediator.Store([new() { Kind = TelemetryKind.Log }], _cancelled.Token));
        Assert.Equal(0, await _context.TelemetryLogs.CountAsync());
    }

    [Fact]
    public async Task CollectionBudgetIsBoundedAndTruncationIsExplicit()
    {
        var _item = new TelemetryItem
        {
            Kind = TelemetryKind.Trace, ServiceName = "api", Name = "large",
            Events = Enumerable.Range(0, 128).Select(_index => new TelemetryTraceEvent
            {
                Name = $"event-{_index}", AttributesJson = JsonSerializer.Serialize(new { large = new string('x', 2048) })
            }).ToList()
        };
        await _mediator.Store([_item], CancellationToken.None);
        var _stored = Assert.Single((await _mediator.Query(new TelemetryQuery(TelemetryKind.Trace), CancellationToken.None)).Items);
        Assert.True(_stored.CollectionsTruncated);
        Assert.Empty(_stored.Events);
    }

    [Fact]
    public async Task DedicatedQueriesUseIndexesAndGlobalQueryUsesUnionAll()
    {
        await using var _context = new TelemetryDbContext(_options);
        Assert.Contains("UNION ALL", _context.QueryTelemetry().Select(_item => new { _item.Kind, _item.Id }).ToQueryString());
        await _context.Database.OpenConnectionAsync();
        foreach (var (_sql, _index) in new[]
        {
            ("SELECT Id FROM TelemetryMetrics WHERE Name='metric' AND ResourceId=1 AND TimestampUtc>=0 ORDER BY TimestampUtc DESC LIMIT 100", "IX_TelemetryMetrics_Name_ResourceId_TimestampUtc"),
            ("SELECT Id FROM TelemetryTraces WHERE TraceId='trace'", "IX_TelemetryTraces_TraceId"),
            ("SELECT Id FROM TelemetryLogs WHERE TimestampUtc>=0 ORDER BY TimestampUtc DESC LIMIT 100", "IX_TelemetryLogs_TimestampUtc"),
            ("SELECT Id FROM TelemetryRequest WHERE ResourceId=1 AND TimestampUtc>=0 ORDER BY TimestampUtc DESC LIMIT 100", "IX_TelemetryRequest_ResourceId_TimestampUtc")
        })
        {
            await using var _command = _context.Database.GetDbConnection().CreateCommand();
            _command.CommandText = "EXPLAIN QUERY PLAN " + _sql;
            await using var _reader = await _command.ExecuteReaderAsync();
            var _plan = new List<string>();
            while (await _reader.ReadAsync())
            {
                _plan.Add(_reader.GetString(3));
            }
            Assert.Contains(_plan, _line => _line.Contains(_index, StringComparison.Ordinal));
            Assert.DoesNotContain(_plan, _line => _line.Contains("TEMP B-TREE", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task NonFiniteMetricFailsWithBrokenRulesAndNoPartialWrite()
    {
        var _result = await _mediator.Send(new StoreTelemetryBatchRequest([
            new() { Kind = TelemetryKind.Log, Name = "log", ServiceName = "api" },
            new() { Kind = TelemetryKind.Metric, NumericValue = double.NaN, Name = "invalid", ServiceName = "api" }
        ]), CancellationToken.None);
        Assert.True(_result.HasError);
        await using var _context = new TelemetryDbContext(_options);
        Assert.Empty(await _context.QueryTelemetry().ToListAsync());
        Assert.Empty(await _context.TelemetryResources.ToListAsync());
    }

    [Fact]
    public async Task MigrationPreservesConfigurationAndDowngradeRecreatesEmptyLegacyTable()
    {
        var _path = Path.Combine(Path.GetTempPath(), $"typed-upgrade-{Guid.NewGuid():N}.db");
        var _options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_path}").Options;
        try
        {
            await using var _context = new TelemetryDbContext(_options);
            const string PREVIOUS_MIGRATION = "20260926133236_IndexHandlerQueries";
            await _context.GetService<IMigrator>().MigrateAsync(PREVIOUS_MIGRATION);
            _context.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = "key", Xml = "<key />" });
            _context.Dashboards.Add(new DashboardDefinition { Name = "dashboard" });
            _context.IngestionApplications.Add(new IngestionApplication { Name = "app", KeyHash = "hash" });
            _context.AlertRules.Add(new AlertRule { Name = "rule" });
            _context.Incidents.Add(new Incident { RuleName = "rule" });
            _context.NotificationDeliveries.Add(new NotificationDelivery { EventKey = "event" });
            await _context.SaveChangesAsync();
            await _context.Database.ExecuteSqlRawAsync("INSERT INTO TelemetryItems (Kind,TimestampUtc,ObservedUtc,ServiceName,Name,ResourceAttributesJson,AttributesJson,DetailsJson) VALUES (1,0,0,'api','old','{{}}','{{}}','{{}}')");
            await _context.Database.MigrateAsync();
            Assert.Empty(await _context.QueryTelemetry().ToListAsync());
            Assert.Equal("key", (await _context.DataProtectionKeys.SingleAsync()).FriendlyName);
            Assert.Equal("dashboard", (await _context.Dashboards.SingleAsync()).Name);
            Assert.Equal("app", (await _context.IngestionApplications.SingleAsync()).Name);
            Assert.Equal("rule", (await _context.AlertRules.SingleAsync()).Name);
            Assert.Equal("rule", (await _context.Incidents.SingleAsync()).RuleName);
            Assert.Equal("event", (await _context.NotificationDeliveries.SingleAsync()).EventKey);
            await _context.GetService<IMigrator>().MigrateAsync(PREVIOUS_MIGRATION);
            await _context.Database.OpenConnectionAsync();
            await using var _command = _context.Database.GetDbConnection().CreateCommand();
            _command.CommandText = "SELECT COUNT(*) FROM TelemetryItems";
            Assert.Equal(0L, await _command.ExecuteScalarAsync());
            Assert.Equal("dashboard", (await _context.Dashboards.SingleAsync()).Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(_path);
        }
    }
}
