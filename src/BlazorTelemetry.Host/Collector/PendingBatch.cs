using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

internal sealed class PendingBatch(IReadOnlyList<TelemetryItem> items)
{
    public IReadOnlyList<TelemetryItem> Items { get; } = items;
    public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public BatchState State { get; set; } = BatchState.Pending;
}
