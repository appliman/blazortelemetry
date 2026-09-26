namespace BlazorTelemetry.Host.Contracts.Results;

public class CommandResult
{
    public IReadOnlyList<BrokenRule> BrokenRules { get; init; } = [];
    public bool HasError => BrokenRules.Count > 0;
    public int ChangeCount { get; init; }
}
