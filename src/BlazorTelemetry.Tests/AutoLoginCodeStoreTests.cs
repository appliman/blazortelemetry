using System.Collections.Concurrent;
using BlazorTelemetry.Host.Authentication;

namespace BlazorTelemetry.Tests;

public sealed class AutoLoginCodeStoreTests
{
    [Fact]
    public void IssuedCodesContain256BitsAndExpireAfterFiveMinutes()
    {
        var _clock = new TestTimeProvider();
        using var _store = new AutoLoginCodeStore(_clock);
        var _key = CreateKey();

        var _code = Assert.IsType<AutoLoginCode>(_store.Issue(_key));

        Assert.Matches("^[A-Za-z0-9_-]{43}$", _code.Code);
        Assert.Equal(32, Convert.FromBase64String(_code.Code.Replace('-', '+').Replace('_', '/') + "=").Length);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(5), _code.ExpiresUtc);
        Assert.True(_store.TryConsume(_code.Code, out var _consumedKey));
        Assert.Equal(_key, _consumedKey);
    }

    [Fact]
    public void CodeRemainsValidImmediatelyBeforeExpiration()
    {
        var _clock = new TestTimeProvider();
        using var _store = new AutoLoginCodeStore(_clock);
        var _code = Assert.IsType<AutoLoginCode>(_store.Issue(CreateKey()));

        _clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));

        Assert.True(_store.TryConsume(_code.Code, out var _key));
        Assert.NotNull(_key);
    }

    [Fact]
    public void CodeIsInvalidAtExactExpiration()
    {
        var _clock = new TestTimeProvider();
        using var _store = new AutoLoginCodeStore(_clock);
        var _code = Assert.IsType<AutoLoginCode>(_store.Issue(CreateKey()));

        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(_store.TryConsume(_code.Code, out var _key));
        Assert.Null(_key);
    }

    [Fact]
    public void ConsumedCodeCannotBeReplayed()
    {
        using var _store = new AutoLoginCodeStore(new TestTimeProvider());
        var _code = Assert.IsType<AutoLoginCode>(_store.Issue(CreateKey()));

        Assert.True(_store.TryConsume(_code.Code, out _));
        Assert.False(_store.TryConsume(_code.Code, out var _key));
        Assert.Null(_key);
    }

    [Fact]
    public void ConcurrentConsumptionSucceedsExactlyOnce()
    {
        using var _store = new AutoLoginCodeStore(new TestTimeProvider());
        var _expectedKey = CreateKey();
        var _code = Assert.IsType<AutoLoginCode>(_store.Issue(_expectedKey));
        var _consumedKeys = new ConcurrentBag<AutoLoginKey>();

        Parallel.For(0, 64, _ =>
        {
            if (_store.TryConsume(_code.Code, out var _key))
            {
                _consumedKeys.Add(Assert.IsType<AutoLoginKey>(_key));
            }
            else
            {
                Assert.Null(_key);
            }
        });

        Assert.Equal(_expectedKey, Assert.Single(_consumedKeys));
    }

    [Fact]
    public void ConcurrentIssuanceStopsAtCapacityAndProducesUniqueCodes()
    {
        using var _store = new AutoLoginCodeStore(new TestTimeProvider());
        var _key = CreateKey();
        var _codes = new ConcurrentBag<string>();

        Parallel.For(0, 2048, _ =>
        {
            var _issued = _store.Issue(_key);
            if (_issued is not null)
            {
                _codes.Add(_issued.Code);
            }
        });

        Assert.Equal(1024, _codes.Count);
        Assert.Equal(1024, _codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Null(_store.Issue(_key));
        Assert.True(_store.TryConsume(_codes.First(), out _));
        Assert.NotNull(_store.Issue(_key));
        Assert.Null(_store.Issue(_key));
    }

    [Fact]
    public void ExpiredCodesReleaseCapacityOnIssuance()
    {
        var _clock = new TestTimeProvider();
        using var _store = new AutoLoginCodeStore(_clock);
        var _key = CreateKey();
        var _expiredCodes = Enumerable.Range(0, 1024)
            .Select(_ => Assert.IsType<AutoLoginCode>(_store.Issue(_key)).Code)
            .ToArray();
        Assert.Null(_store.Issue(_key));

        _clock.Advance(TimeSpan.FromMinutes(5));

        for (var _index = 0; _index < 1024; _index++)
        {
            Assert.NotNull(_store.Issue(_key));
        }
        Assert.Null(_store.Issue(_key));
        foreach (var _code in _expiredCodes)
        {
            Assert.False(_store.TryConsume(_code, out var _consumedKey));
            Assert.Null(_consumedKey);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("<script>alert(1)</script>")]
    public void MissingUnknownAndMalformedCodesAreRejected(string? code)
    {
        using var _store = new AutoLoginCodeStore(new TestTimeProvider());

        Assert.False(_store.TryConsume(code, out var _key));
        Assert.Null(_key);
    }

    [Fact]
    public void DisposedStoreRejectsIssuanceAndConsumption()
    {
        var _store = new AutoLoginCodeStore(new TestTimeProvider());
        var _code = Assert.IsType<AutoLoginCode>(_store.Issue(CreateKey()));

        _store.Dispose();

        Assert.Null(_store.Issue(CreateKey()));
        Assert.False(_store.TryConsume(_code.Code, out var _key));
        Assert.Null(_key);
        _store.Dispose();
    }

    private static AutoLoginKey CreateKey() => new("database", "test-key-id", "test-key-hash", "Test key");
}
