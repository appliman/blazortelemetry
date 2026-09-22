using BlazorTelemetry.Core;

namespace BlazorTelemetry.Tests;

public sealed class TelemetryTimeRangeTests
{
    [Fact]
    public void DefaultRangeFollowsTheLastHour()
    {
        var _now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var _range = new TelemetryTimeRange();
        Assert.True(_range.TryResolve(_now, out var _from, out var _to));
        Assert.Equal(_now.AddHours(-1), _from);
        Assert.Equal(_now, _to);
        Assert.True(_range.TryResolve(_now.AddMinutes(1), out _from, out _to));
        Assert.Equal(_now.AddMinutes(-59), _from);
    }

    [Fact]
    public void AbsoluteRangeIsFixedAndNormalizesOffsets()
    {
        var _range = new TelemetryTimeRange("2026-09-22T10:00:00+02:00", "2026-09-22 09:00");
        Assert.True(_range.TryResolve(DateTimeOffset.UtcNow, out var _from, out var _to));
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero), _from);
        Assert.Equal(_from.AddHours(1), _to);
    }

    [Theory]
    [InlineData("now-5m", "now", 5)]
    [InlineData("now-3h", "now-1h", 120)]
    [InlineData("now-7d", "now", 10080)]
    public void RelativeRangesResolveAgainstTheSameInstant(string _start, string _end, int _minutes)
    {
        Assert.True(new TelemetryTimeRange(_start, _end).TryResolve(DateTimeOffset.UtcNow, out var _from, out var _to));
        Assert.Equal(TimeSpan.FromMinutes(_minutes), _to - _from);
    }

    [Theory]
    [InlineData("", "now")]
    [InlineData("now-1x", "now")]
    [InlineData("now-999999999999999999999d", "now")]
    [InlineData("now", "now")]
    [InlineData("now-1h", "now-2h")]
    [InlineData("2026-02-30 10:00", "now")]
    public void InvalidRangesAreRejected(string _from, string _to)
    {
        Assert.False(new TelemetryTimeRange(_from, _to).TryResolve(DateTimeOffset.UtcNow, out _, out _));
    }
}
