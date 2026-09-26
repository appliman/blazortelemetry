namespace BlazorTelemetry.Tests;

internal sealed class CoordinatorTestGate
{
    public bool PauseBeforeStart { get; set; }
    public TaskCompletionSource<bool> AllowStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Commit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
