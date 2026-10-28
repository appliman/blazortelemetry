using BlazorTelemetry.Core;
using System.Text.Json;

namespace BlazorTelemetry.Tests;

public sealed class ProcessMetricSeriesTests
{
    [Fact]
    public void ActiveRequestsUsesRealMeasurementsAcrossTheWholeSelectedPeriod()
    {
        var _from = DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        var _to = _from.AddHours(1);
        var _items = Enumerable.Range(0, 61)
            .Select(_index => Item("http.server.active_requests", _from.AddMinutes(_index), _index, _unit: "{request}"))
            .ToList();
        _items.Add(Item("http.server.active_requests", _from.AddTicks(-1), 1000));
        _items.Add(Item("http.server.active_requests", _to.AddTicks(1), 1000));
        _items.Add(Item("http.server.request.duration", _from, 1000));
        _items.Add(Item("http.server.active_requests", _from.AddSeconds(1), double.NaN));

        var _series = ProcessMetricSeries.ActiveRequests(_items, _from, _to);

        Assert.Equal(61, _series.Count);
        Assert.Equal(_from, _series[0].TimestampUtc);
        Assert.Equal(0, _series[0].Value);
        Assert.Equal(_to, _series[^1].TimestampUtc);
        Assert.Equal(60, _series[^1].Value);
    }

    [Fact]
    public void ActiveRequestsAggregatesLatestSamplePerSeriesWithoutCountingTraces()
    {
        var _from = DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        TelemetryItem[] _items = [
            Item("http.server.active_requests", _from, 100, "a"),
            Item("http.server.active_requests", _from.AddMilliseconds(100), 2, "a"),
            Item("http.server.active_requests", _from.AddMilliseconds(200), 3, "b"),
            new() { Kind = TelemetryKind.Trace, TimestampUtc = _from, Name = "GET /" }
        ];

        var _point = Assert.Single(ProcessMetricSeries.ActiveRequests(_items, _from, _from.AddHours(1)));

        Assert.Equal(5, _point.Value);
        Assert.Equal(_from.AddMilliseconds(200), _point.TimestampUtc);
        Assert.Empty(ProcessMetricSeries.ActiveRequests([_items[^1]], _from, _from.AddHours(1)));
    }

    [Fact]
    public void CalculatesRatesPerProcessAndPerDirectionBeforeAggregation()
    {
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        var _items = new List<TelemetryItem>();
        foreach (var _instance in new[] { "a", "b" })
        {
            _items.Add(Item("process.cpu.count", _from, 2, _instance));
            _items.Add(Item("process.cpu.time", _from.AddSeconds(-10), 500, _instance, "{\"cpu.mode\":\"user\"}", "s"));
            _items.Add(Item("process.cpu.time", _from, 505, _instance, "{\"cpu.mode\":\"user\"}", "s"));
            _items.Add(Item("process.memory.usage", _from.AddSeconds(-10), 10 * 1024 * 1024, _instance));
            _items.Add(Item("process.memory.usage", _from, 2 * 1024 * 1024, _instance));
            _items.Add(Item("process.network.io", _from.AddSeconds(-10), 100 * 1024, _instance, "{\"network.io.direction\":\"receive\"}"));
            _items.Add(Item("process.network.io", _from, 120 * 1024, _instance, "{\"network.io.direction\":\"receive\"}"));
            _items.Add(Item("process.disk.io", _from.AddSeconds(-10), 90 * 1024, _instance, "{\"disk.io.direction\":\"write\"}"));
            _items.Add(Item("process.disk.io", _from, 100 * 1024, _instance, "{\"disk.io.direction\":\"write\"}"));
        }
        _items.Add(Item("system.memory.usage", _from, 1024 * 1024 * 1024));
        var _series = ProcessMetricSeries.Create(_items, _from, _from.AddSeconds(30));
        Assert.Equal(50, Assert.Single(_series.Cpu).Value);
        Assert.Equal(4, Assert.Single(_series.Memory).Value);
        Assert.Equal(4, Assert.Single(_series.Received).Value);
        Assert.Equal(2, Assert.Single(_series.Written).Value);
        Assert.All(_series.Memory, _point => Assert.True(_point.TimestampUtc >= _from));
        Assert.False(_series.IncludesNonDiskIo);
    }

    [Fact]
    public void HandlesCounterResetsAndDeltaTemporality()
    {
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        var _before = Item("process.network.io", _from.AddSeconds(-10), 90000);
        var _reset = Item("process.network.io", _from, 2048);
        var _delta = Item("process.network.io", _from.AddSeconds(10), 10240);
        _delta.AggregationTemporality = "Delta";
        _delta.StartTimeUtc = _from;
        var _rates = MetricCounter.Rates([_before, _reset, _delta], _from, _from.AddSeconds(20));
        Assert.Equal(204.8, _rates[0].Value, 4);
        Assert.Equal(1024, _rates[1].Value);
        Assert.Null(MetricCounter.Increment(Item("counter", _from, 99999), null, _from));
    }

    [Fact]
    public void SelectsFallbackMetricsPerProcessAndLabelsWindowsIoHonestly()
    {
        var _from = DateTimeOffset.Parse("2026-09-22T10:00:00Z");
        var _series = ProcessMetricSeries.Create([
            Item("process.memory.usage", _from, 1024 * 1024, "a"),
            Item("dotnet.process.memory.working_set", _from, 2 * 1024 * 1024, "b"),
            Item("blazortelemetry.process.io", _from.AddSeconds(-10), 0, "b", "{\"disk.io.direction\":\"read\"}"),
            Item("blazortelemetry.process.io", _from, 10240, "b", "{\"disk.io.direction\":\"read\"}")
        ], _from, _from.AddSeconds(30));
        Assert.Equal(3, Assert.Single(_series.Memory).Value);
        Assert.Equal(1, Assert.Single(_series.Read).Value);
        Assert.True(_series.IncludesNonDiskIo);
    }

    internal static TelemetryItem Item(string _name, DateTimeOffset _time, double _value, string _instance = "a", string _attributes = "{}", string _unit = "By") => new()
    {
        Kind = TelemetryKind.Metric, Name = _name, TimestampUtc = _time, NumericValue = _value, Unit = _unit,
        ServiceName = "app", ResourceAttributesJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["service.instance.id"] = _instance }),
        AttributesJson = _attributes, MetricType = "sum"
    };
}
