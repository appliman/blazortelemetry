namespace BlazorTelemetry.Host.Authentication;

public sealed class UserAttemptLimiter(TimeProvider timeProvider) : IDisposable
{
    private const int MAXIMUM_ENTRIES = 4096;
    private readonly Dictionary<string, (DateTimeOffset Until, int Count)> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool Allow(string identifier, string? source = null) => AllowKey("account:" + identifier.Trim().ToUpperInvariant() + "|" + source, 5);

    public bool AllowSource(string source) => AllowKey("source:" + source, 30);

    public void Dispose()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private bool AllowKey(string key, int limit)
    {
        if (key.Length > 350)
        {
            return false;
        }

        lock (_gate)
        {
            var _now = timeProvider.GetUtcNow();
            if (!_entries.TryGetValue(key, out var _entry) || _entry.Until <= _now)
            {
                if (_entries.Count >= MAXIMUM_ENTRIES)
                {
                    foreach (var _expired in _entries.Where(entry => entry.Value.Until <= _now).Select(entry => entry.Key).ToArray())
                    {
                        _entries.Remove(_expired);
                    }
                }
                if (!_entries.ContainsKey(key) && _entries.Count >= MAXIMUM_ENTRIES)
                {
                    return false;
                }
                _entry = (_now.AddMinutes(1), 0);
            }
            if (_entry.Count >= limit)
            {
                return false;
            }
            _entries[key] = (_entry.Until, _entry.Count + 1);
            return true;
        }
    }
}
