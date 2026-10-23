using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace BlazorTelemetry.Host.Authentication;

public sealed class AutoLoginCodeStore : IDisposable
{
    private const int MAXIMUM_CODES = 1_024;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<string, (AutoLoginKey Key, DateTimeOffset ExpiresUtc)> _codes = new(StringComparer.Ordinal);
    private bool _disposed;

    public AutoLoginCodeStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _timer = timeProvider.CreateTimer(_ => RemoveExpired(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public AutoLoginCode? Issue(AutoLoginKey key)
    {
        lock (_gate)
        {
            RemoveExpired();
            if (_disposed || _codes.Count >= MAXIMUM_CODES)
            {
                return null;
            }

            var _code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var _expiresUtc = _timeProvider.GetUtcNow().AddMinutes(5);
            _codes.Add(Hash(_code), (key, _expiresUtc));
            return new AutoLoginCode(_code, _expiresUtc);
        }
    }

    public bool TryConsume(string? code, out AutoLoginKey? key)
    {
        key = null;
        if (code is null || code.Length != 43 || !code.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_'))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed || !_codes.Remove(Hash(code), out var _ticket) || _ticket.ExpiresUtc <= _timeProvider.GetUtcNow())
            {
                return false;
            }

            key = _ticket.Key;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _codes.Clear();
        }
        _timer.Dispose();
    }

    private void RemoveExpired()
    {
        lock (_gate)
        {
            var _now = _timeProvider.GetUtcNow();
            foreach (var _hash in _codes.Where(value => value.Value.ExpiresUtc <= _now).Select(value => value.Key).ToArray())
            {
                _codes.Remove(_hash);
            }
        }
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
}
