namespace BlazorTelemetry.AspNetCore;

public sealed class CollectorCounters
{
    private long _received;
    private long _persisted;
    private long _rejected;
    private long _purged;
    private int _queueDepth;

    public long Received => Interlocked.Read(ref _received);
    public long Persisted => Interlocked.Read(ref _persisted);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Purged => Interlocked.Read(ref _purged);
    public int QueueDepth => Volatile.Read(ref _queueDepth);

    internal void AddReceived(int count) => Interlocked.Add(ref _received, count);
    internal void AddPersisted(int count) => Interlocked.Add(ref _persisted, count);
    internal void AddRejected(int count) => Interlocked.Add(ref _rejected, count);
    internal void AddPurged(int count) => Interlocked.Add(ref _purged, count);
    internal void ChangeQueueDepth(int delta) => Interlocked.Add(ref _queueDepth, delta);
}
