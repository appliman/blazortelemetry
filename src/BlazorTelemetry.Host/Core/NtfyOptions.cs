namespace BlazorTelemetry.Core;

public sealed class NtfyOptions
{
    public string ServerUrl { get; set; } = "https://ntfy.sh";
    public string? Topic { get; set; }
    public string? AccessToken { get; set; }
}
