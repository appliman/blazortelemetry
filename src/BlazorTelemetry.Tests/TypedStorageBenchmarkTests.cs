using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace BlazorTelemetry.Tests;

public sealed class TypedStorageBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RepresentativeBatchMeasuresTypedStorageAgainstLegacySchema()
    {
        var _typedPath = Path.Combine(Path.GetTempPath(), $"typed-benchmark-{Guid.NewGuid():N}.db");
        var _legacyPath = Path.Combine(Path.GetTempPath(), $"legacy-benchmark-{Guid.NewGuid():N}.db");
        var _now = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        var _resource = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["service.name"] = "benchmark", ["service.version"] = "1.0.0", ["service.instance.id"] = "instance-1",
            ["deployment.environment.name"] = "test", ["telemetry.sdk.name"] = "opentelemetry",
            ["telemetry.sdk.language"] = "dotnet", ["telemetry.sdk.version"] = "1.18.0"
        });
        var _items = Enumerable.Range(0, 4000).Select(_index => new TelemetryItem
        {
            Kind = (_index % 100) switch { < 1 => TelemetryKind.Request, < 3 => TelemetryKind.Log, < 33 => TelemetryKind.Metric, _ => TelemetryKind.Trace },
            TimestampUtc = _now.AddMilliseconds(_index), ObservedUtc = _now, ServiceName = "benchmark", Name = "operation",
            ResourceAttributesJson = _resource,
            AttributesJson = JsonSerializer.Serialize(new { operation = "select", statement = new string('x', 400) }),
            Body = "description", ScopeName = "scope", SpanKind = "Client", DurationMs = 5,
            NumericValue = 42, MetricType = "sum", Unit = "s", AggregationTemporality = "Cumulative", IsMonotonic = true,
            TraceId = _index.ToString("x32"), SpanId = _index.ToString("x16"), Fingerprint = _index % 100 is >= 1 and < 3 ? null : $"sample-{_index}"
        }).ToArray();
        try
        {
            var _typedOptions = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_typedPath}").Options;
            await using var _typed = new TelemetryDbContext(_typedOptions);
            await _typed.Database.MigrateAsync();
            var _typedTimer = Stopwatch.StartNew();
            Assert.Equal(_items.Length, await _typed.StoreTelemetry(_items, CancellationToken.None));
            _typedTimer.Stop();
            Assert.Equal(_items.Length, await _typed.QueryTelemetry().CountAsync());
            Assert.Equal(1, await _typed.TelemetryResources.CountAsync());
            var _legacyOptions = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_legacyPath}").Options;
            await using var _legacy = new TelemetryDbContext(_legacyOptions);
            await _legacy.GetService<IMigrator>().MigrateAsync("20260926133236_IndexHandlerQueries");
            await _legacy.Database.OpenConnectionAsync();
            var _connection = (SqliteConnection)_legacy.Database.GetDbConnection();
            var _legacyTimer = Stopwatch.StartNew();
            await using (var _transaction = (SqliteTransaction)await _connection.BeginTransactionAsync())
            {
                await using var _command = _connection.CreateCommand();
                _command.Transaction = _transaction;
                _command.CommandText = "INSERT INTO TelemetryItems (Kind,TimestampUtc,ObservedUtc,ServiceName,Name,Body,TraceId,SpanId,DurationMs,Unit,MetricType,NumericValue,ResourceAttributesJson,AttributesJson,DetailsJson,Fingerprint) VALUES ($kind,$time,$observed,$service,$name,$body,$trace,$span,$duration,$unit,$type,$value,$resource,$attributes,$details,$fingerprint)";
                foreach (var _name in new[] { "kind", "time", "observed", "service", "name", "body", "trace", "span", "duration", "unit", "type", "value", "resource", "attributes", "details", "fingerprint" })
                {
                    _command.Parameters.Add(new SqliteParameter("$" + _name, DBNull.Value));
                }
                var _converter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter();
                var _encode = _converter.ConvertToProviderExpression.Compile();
                foreach (var _item in _items)
                {
                    _command.Parameters["$kind"].Value = (int)_item.Kind;
                    _command.Parameters["$time"].Value = _encode(_item.TimestampUtc);
                    _command.Parameters["$observed"].Value = _encode(_item.ObservedUtc);
                    _command.Parameters["$service"].Value = _item.ServiceName;
                    _command.Parameters["$name"].Value = _item.Name;
                    _command.Parameters["$body"].Value = _item.Body!;
                    _command.Parameters["$trace"].Value = _item.Kind == TelemetryKind.Metric ? DBNull.Value : _item.TraceId!;
                    _command.Parameters["$span"].Value = _item.Kind == TelemetryKind.Metric ? DBNull.Value : _item.SpanId!;
                    _command.Parameters["$duration"].Value = _item.Kind is TelemetryKind.Trace or TelemetryKind.Request ? 5 : DBNull.Value;
                    _command.Parameters["$unit"].Value = _item.Kind == TelemetryKind.Metric ? _item.Unit! : DBNull.Value;
                    _command.Parameters["$type"].Value = _item.Kind == TelemetryKind.Metric ? _item.MetricType! : DBNull.Value;
                    _command.Parameters["$value"].Value = _item.Kind == TelemetryKind.Metric ? 42 : DBNull.Value;
                    _command.Parameters["$resource"].Value = _resource;
                    _command.Parameters["$attributes"].Value = _item.AttributesJson;
                    _command.Parameters["$details"].Value = _item.Kind == TelemetryKind.Metric
                        ? JsonSerializer.Serialize(new { scope = "scope", data = new { aggregationTemporality = "Cumulative", isMonotonic = true, exemplars = Array.Empty<object>() } })
                        : JsonSerializer.Serialize(new { scope = "scope", kind = "Client", events = Array.Empty<object>(), links = Array.Empty<object>() });
                    _command.Parameters["$fingerprint"].Value = _item.Kind == TelemetryKind.Log ? DBNull.Value : _item.Fingerprint!;
                    await _command.ExecuteNonQueryAsync();
                }
                await _transaction.CommitAsync();
            }
            _legacyTimer.Stop();
            await _typed.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            await _legacy.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            var _typedBytes = new FileInfo(_typedPath).Length;
            var _legacyBytes = new FileInfo(_legacyPath).Length;
            output.WriteLine($"Rows: {_items.Length}; typed: {_typedBytes:N0} bytes, {_typedTimer.Elapsed.TotalSeconds:F3}s; legacy: {_legacyBytes:N0} bytes, {_legacyTimer.Elapsed.TotalSeconds:F3}s; size reduction: {1d - (double)_typedBytes / _legacyBytes:P1}.");
            output.WriteLine("Legacy uses parameterized inserts; typed uses EF, resource resolution and deduplication. Times describe this local run and are not equivalent ingestion pipelines.");
            Assert.True(_typedBytes < _legacyBytes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_typedPath);
            File.Delete(_legacyPath);
        }
    }
}
