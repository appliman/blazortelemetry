namespace BlazorTelemetry.Core;

public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string[] Recipients { get; set; } = [];
}
