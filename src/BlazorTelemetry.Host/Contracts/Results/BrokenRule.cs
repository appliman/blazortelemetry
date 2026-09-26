namespace BlazorTelemetry.Host.Contracts.Results;

public sealed record BrokenRule(string Code, string PropertyName, string Message);
