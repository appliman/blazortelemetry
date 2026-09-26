namespace BlazorTelemetry.Host.Contracts.Results;

public sealed class PersistResult : CommandResult
{
    public long Id { get; init; }
    public bool IsNewEntity { get; init; }
}
