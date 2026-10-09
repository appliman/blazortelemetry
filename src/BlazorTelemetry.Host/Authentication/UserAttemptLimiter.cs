using Microsoft.Extensions.Caching.Memory;

namespace BlazorTelemetry.Host.Authentication;

public sealed class UserAttemptLimiter(TimeProvider timeProvider) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 4096 });
    private readonly object _gate = new();

    public bool Allow(string identifier)
    {
        lock (_gate)
        {
            var _key = identifier.Trim().ToUpperInvariant();
            var _now = timeProvider.GetUtcNow();
            var _entry = _cache.Get<(DateTimeOffset Until, int Count)?>(_key);
            if (_entry is null || _entry.Value.Until <= _now)
            {
                _entry = (_now.AddMinutes(1), 0);
            }
            if (_entry.Value.Count >= 5)
            {
                return false;
            }
            _cache.Set(_key, (_entry.Value.Until, _entry.Value.Count + 1), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpiration = _entry.Value.Until });
            return true;
        }
    }

    public void Dispose() => _cache.Dispose();
}
