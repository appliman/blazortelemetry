namespace BlazorTelemetry.Tests;

internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) { _now += duration; }
}
