namespace BlazorTelemetry.Host.Contracts.Results;

public sealed class QueryResult<T> : CommandResult
{
    public T? Data { get; init; }
}
