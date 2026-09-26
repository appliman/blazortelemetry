namespace BlazorTelemetry.Core;

public sealed record ProcessMetricSeries(
    IReadOnlyList<MetricSeriesPoint> Cpu,
    IReadOnlyList<MetricSeriesPoint> Memory,
    IReadOnlyList<MetricSeriesPoint> Received,
    IReadOnlyList<MetricSeriesPoint> Sent,
    IReadOnlyList<MetricSeriesPoint> Read,
    IReadOnlyList<MetricSeriesPoint> Written,
    bool IncludesNonDiskIo)
{
    public static ProcessMetricSeries Empty { get; } = new([], [], [], [], [], [], false);

    public static ProcessMetricSeries Create(IReadOnlyList<TelemetryItem> _items, DateTimeOffset _fromUtc, DateTimeOffset _toUtc)
    {
        var _cpu = new List<MetricSeriesPoint>();
        var _memory = new List<MetricSeriesPoint>();
        var _received = new List<MetricSeriesPoint>();
        var _sent = new List<MetricSeriesPoint>();
        var _read = new List<MetricSeriesPoint>();
        var _written = new List<MetricSeriesPoint>();
        var _includesNonDiskIo = false;
        foreach (var _process in _items.Where(_item => _item.NumericValue is double _value && double.IsFinite(_value))
            .GroupBy(_item => (_item.ServiceName, _item.ResourceAttributesJson)))
        {
            var _utilization = Select(_process, "process.cpu.utilization");
            if (_utilization.Count > 0)
            {
                _cpu.AddRange(Gauges(_utilization, _fromUtc, _toUtc, _item => _item.NumericValue!.Value * (_item.Unit == "%" ? 1 : 100)));
            }
            else
            {
                var _cpuTime = Select(_process, "process.cpu.time", "dotnet.process.cpu.time");
                var _cpuCounts = Select(_process, "process.cpu.count", "dotnet.process.cpu.count").OrderBy(_item => _item.TimestampUtc).ToArray();
                foreach (var _series in _cpuTime.GroupBy(_item => _item.Unit))
                {
                    var _multiplier = _series.Key switch { "ms" => .001, "us" => .000001, "ns" => .000000001, _ => 1 };
                    foreach (var _point in MetricCounter.Rates(_series, _fromUtc, _toUtc, _multiplier))
                    {
                        var _count = _cpuCounts.LastOrDefault(_item => _item.TimestampUtc <= _point.TimestampUtc)?.NumericValue ?? 1;
                        _cpu.Add(new(_point.TimestampUtc, _point.Value * 100 / Math.Max(1, _count)));
                    }
                }
            }
            _memory.AddRange(Gauges(Select(_process, "process.memory.usage", "dotnet.process.memory.working_set",
                "dotnet.gc.last_collection.memory.committed_size", "process.runtime.dotnet.gc.committed_memory.size", "process.runtime.dotnet.gc.heap.size"),
                _fromUtc, _toUtc, _item => Bytes(_item.NumericValue!.Value, _item.Unit) / 1024 / 1024));

            var _network = Select(_process, "process.network.io");
            _received.AddRange(IoRates(_network, "network.io.direction", ["receive", "in", "rx"], _fromUtc, _toUtc));
            _sent.AddRange(IoRates(_network, "network.io.direction", ["transmit", "out", "tx"], _fromUtc, _toUtc));
            var _io = Select(_process, "process.disk.io", "blazortelemetry.process.io");
            _includesNonDiskIo |= _io.Any(_item => _item.Name == "blazortelemetry.process.io");
            _read.AddRange(IoRates(_io, "disk.io.direction", ["read"], _fromUtc, _toUtc));
            _written.AddRange(IoRates(_io, "disk.io.direction", ["write"], _fromUtc, _toUtc));
        }
        return new(Aggregate(_cpu), Aggregate(_memory), Aggregate(_received), Aggregate(_sent), Aggregate(_read), Aggregate(_written), _includesNonDiskIo);
    }

    private static IReadOnlyList<TelemetryItem> Select(IEnumerable<TelemetryItem> _items, params string[] _names)
    {
        foreach (var _name in _names)
        {
            var _matches = _items.Where(_item => _item.Name == _name).ToArray();
            if (_matches.Length > 0)
            {
                return _matches;
            }
        }
        return [];
    }

    private static IEnumerable<MetricSeriesPoint> Gauges(IEnumerable<TelemetryItem> _items, DateTimeOffset _fromUtc, DateTimeOffset _toUtc, Func<TelemetryItem, double> _value)
    {
        return _items.Where(_item => _item.TimestampUtc >= _fromUtc && _item.TimestampUtc <= _toUtc)
            .GroupBy(_item => (Series: MetricCounter.SeriesKey(_item), Timestamp: ToSecond(_item.TimestampUtc)))
            .Select(_group => new MetricSeriesPoint(_group.Key.Timestamp, _value(_group.MaxBy(_item => _item.TimestampUtc)!)));
    }

    private static IEnumerable<MetricSeriesPoint> IoRates(IEnumerable<TelemetryItem> _items, string _attribute, string[] _directions, DateTimeOffset _fromUtc, DateTimeOffset _toUtc)
    {
        foreach (var _unit in _items.Where(_item => _directions.Contains(Attribute(_item, _attribute) ?? Attribute(_item, "direction"), StringComparer.OrdinalIgnoreCase)).GroupBy(_item => _item.Unit))
        {
            foreach (var _point in MetricCounter.Rates(_unit, _fromUtc, _toUtc, Bytes(1, _unit.Key) / 1024))
            {
                yield return _point;
            }
        }
    }

    private static string? Attribute(TelemetryItem _item, string _name)
    {
        try
        {
            using var _document = System.Text.Json.JsonDocument.Parse(_item.AttributesJson);
            return _document.RootElement.TryGetProperty(_name, out var _value) ? _value.ToString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<MetricSeriesPoint> Aggregate(IEnumerable<MetricSeriesPoint> _points) => _points
        .GroupBy(_point => ToSecond(_point.TimestampUtc)).OrderBy(_group => _group.Key).TakeLast(40)
        .Select(_group => new MetricSeriesPoint(_group.Key, _group.Sum(_point => _point.Value))).ToArray();

    private static DateTimeOffset ToSecond(DateTimeOffset _value) => new(_value.UtcTicks - _value.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static double Bytes(double _value, string? _unit) => _unit?.ToLowerInvariant() switch
    {
        "kby" or "kib" => _value * 1024,
        "mby" or "mib" => _value * 1024 * 1024,
        "gby" or "gib" => _value * 1024 * 1024 * 1024,
        _ => _value
    };
}
