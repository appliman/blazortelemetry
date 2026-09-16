namespace BlazorTelemetry.Core;

public sealed record CollectorHealth(
    long Received,
    long Persisted,
    long Rejected,
    long Purged,
    int QueueDepth,
    long DatabaseBytes,
    long WalBytes,
    long AvailableDiskBytes);
