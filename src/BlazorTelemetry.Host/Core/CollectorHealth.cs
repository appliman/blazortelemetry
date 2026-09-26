namespace BlazorTelemetry.Core;

public sealed record CollectorHealth(
    long Received,
    long Persisted,
    long Rejected,
    long Purged,
    int QueueDepth,
    long DatabaseBytes,
    long WalBytes,
    long AvailableDiskBytes)
{
    public bool ServerGarbageCollection { get; init; }
    public long ProcessWorkingSetBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long ManagedHeapCommittedBytes { get; init; }
    public long ManagedHeapFragmentedBytes { get; init; }
    public long ManagedTotalAllocatedBytes { get; init; }
    public long GcMemoryLoadBytes { get; init; }
    public long GcTotalAvailableMemoryBytes { get; init; }
}
