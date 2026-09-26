namespace BlazorTelemetry.Core;

public sealed class WebhookOptions
{
    public string? Url { get; set; }
    public string? BearerToken { get; set; }
}
