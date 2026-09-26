using System.Text.Json;

namespace BlazorTelemetry.Core;

public static class MetricCounter
{
    public static string SeriesKey(TelemetryItem _item) => JsonSerializer.Serialize(new[]
    {
        _item.ServiceName, _item.Name, CanonicalJson(_item.ResourceAttributesJson),
        CanonicalJson(_item.AttributesJson), ReadScope(_item.DetailsJson)
    });

    public static bool IsDelta(TelemetryItem _item) => ReadData(_item.DetailsJson, "aggregationTemporality")
        ?.Equals("Delta", StringComparison.OrdinalIgnoreCase) == true;

    public static DateTimeOffset? StartTime(TelemetryItem _item)
    {
        var _text = ReadData(_item.DetailsJson, "startTimeUnixNano");
        return ulong.TryParse(_text, out var _nano) && _nano > 0
            ? DateTimeOffset.UnixEpoch.AddTicks((long)(_nano / 100))
            : null;
    }

    public static double? Increment(TelemetryItem _current, TelemetryItem? _previous, DateTimeOffset _fromUtc)
    {
        if (_current.NumericValue is not double _value || !double.IsFinite(_value) || _value < 0)
        {
            return null;
        }
        if (IsDelta(_current))
        {
            return _value;
        }

        var _start = StartTime(_current);
        if (_previous?.NumericValue is not double _previousValue || IsDelta(_previous))
        {
            // A legacy first sample is a baseline, never a lifetime total for the selected period.
            return _start >= _fromUtc && _start < _current.TimestampUtc ? _value : null;
        }
        if (_current.TimestampUtc <= _previous.TimestampUtc)
        {
            return null;
        }
        var _previousStart = StartTime(_previous);
        if (_start.HasValue && _start != _previousStart && (_previousStart.HasValue || _start > _previous.TimestampUtc) || _value < _previousValue)
        {
            return _value;
        }

        return _value - _previousValue;
    }

    public static IReadOnlyList<MetricSeriesPoint> Rates(IEnumerable<TelemetryItem> _items, DateTimeOffset _fromUtc, DateTimeOffset _toUtc, double _multiplier = 1)
    {
        var _result = new List<MetricSeriesPoint>();
        foreach (var _series in _items.GroupBy(SeriesKey))
        {
            var _seriesPoints = new List<MetricSeriesPoint>();
            TelemetryItem? _previous = null;
            foreach (var _item in _series.OrderBy(_item => _item.TimestampUtc))
            {
                var _start = IsDelta(_item) ? StartTime(_item) : _previous?.TimestampUtc;
                var _increment = Increment(_item, _previous, _fromUtc);
                if (_item.TimestampUtc >= _fromUtc && _item.TimestampUtc <= _toUtc && _start.HasValue
                    && _increment.HasValue && _item.TimestampUtc > _start.Value)
                {
                    _seriesPoints.Add(new(_item.TimestampUtc, _increment.Value / (_item.TimestampUtc - _start.Value).TotalSeconds * _multiplier));
                }
                _previous = _item;
            }
            _result.AddRange(_seriesPoints.GroupBy(_point => _point.TimestampUtc.ToUnixTimeSeconds())
                .Select(_group => new MetricSeriesPoint(DateTimeOffset.FromUnixTimeSeconds(_group.Key), _group.Average(_point => _point.Value))));
        }
        return _result;
    }

    private static string CanonicalJson(string _json)
    {
        try
        {
            using var _document = JsonDocument.Parse(_json);
            return JsonSerializer.Serialize(_document.RootElement.EnumerateObject().OrderBy(_property => _property.Name)
                .ToDictionary(_property => _property.Name, _property => _property.Value));
        }
        catch (JsonException)
        {
            return _json;
        }
    }

    private static string? ReadData(string _json, string _property)
    {
        try
        {
            using var _document = JsonDocument.Parse(_json);
            return _document.RootElement.TryGetProperty("data", out var _data)
                && _data.ValueKind == JsonValueKind.Object && _data.TryGetProperty(_property, out var _value) ? _value.ToString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadScope(string _json)
    {
        try
        {
            using var _document = JsonDocument.Parse(_json);
            return _document.RootElement.TryGetProperty("scope", out var _scope) ? _scope.ToString() : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
