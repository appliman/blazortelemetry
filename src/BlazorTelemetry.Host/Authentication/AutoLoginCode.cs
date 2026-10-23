namespace BlazorTelemetry.Host.Authentication;

public sealed record AutoLoginCode(string Code, DateTimeOffset ExpiresUtc);
