using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

internal sealed record IngestionBatch(
    IReadOnlyList<TelemetryItem> Items,
    TaskCompletionSource<bool> Completion);
