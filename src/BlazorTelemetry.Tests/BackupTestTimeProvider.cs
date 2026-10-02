namespace BlazorTelemetry.Tests;

internal sealed class BackupTestTimeProvider : TimeProvider
{
    private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    }

    public void Advance(TimeSpan duration)
    {
        Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
}
