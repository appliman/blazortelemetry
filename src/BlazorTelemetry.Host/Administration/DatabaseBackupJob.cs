namespace BlazorTelemetry.Host.Administration;

public sealed class DatabaseBackupJob(string owner)
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid Id { get; } = Guid.NewGuid();
    public string Owner { get; } = owner;
    public string FileName { get; } = $"blazor-telemetry-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
    public Task<bool> Completion => _completion.Task;
    internal string? FilePath { get; set; }
    internal DateTimeOffset ExpiresUtc { get; set; } = DateTimeOffset.MaxValue;

    internal void Complete(bool succeeded)
    {
        _completion.TrySetResult(succeeded);
    }
}
