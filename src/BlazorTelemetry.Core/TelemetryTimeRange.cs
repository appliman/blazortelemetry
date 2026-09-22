using System.Globalization;

namespace BlazorTelemetry.Core;

public sealed record TelemetryTimeRange(string From = "now-1h", string To = "now")
{
    public bool TryResolve(DateTimeOffset _now, out DateTimeOffset _fromUtc, out DateTimeOffset _toUtc)
    {
        var _validFrom = TryParse(From, _now, out _fromUtc);
        var _validTo = TryParse(To, _now, out _toUtc);
        return _validFrom && _validTo && _fromUtc < _toUtc;
    }

    private static bool TryParse(string _value, DateTimeOffset _now, out DateTimeOffset _utc)
    {
        _utc = default;
        _value = _value.Trim();
        if (_value.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            _utc = _now;
            return true;
        }

        if (_value.StartsWith("now-", StringComparison.OrdinalIgnoreCase) && _value.Length > 5)
        {
            if (!double.TryParse(_value.AsSpan(4, _value.Length - 5), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var _amount) || !double.IsFinite(_amount) || _amount <= 0)
            {
                return false;
            }

            var _seconds = char.ToLowerInvariant(_value[^1]) switch
            {
                's' => _amount,
                'm' => _amount * 60,
                'h' => _amount * 3600,
                'd' => _amount * 86400,
                _ => double.NaN
            };
            if (!double.IsFinite(_seconds) || _seconds > (_now - DateTimeOffset.MinValue).TotalSeconds)
            {
                return false;
            }

            _utc = _now.AddSeconds(-_seconds);
            return true;
        }

        return DateTimeOffset.TryParseExact(_value,
            ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _utc);
    }
}
