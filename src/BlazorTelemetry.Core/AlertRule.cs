namespace BlazorTelemetry.Core;

public sealed class AlertRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServiceName { get; set; } = "*";
    public AlertRuleType Type { get; set; }
    public string? Query { get; set; }
    public double Threshold { get; set; }
    public int WindowMinutes { get; set; } = 5;
    public int ConfirmationMinutes { get; set; } = 1;
    public string Severity { get; set; } = "warning";
    public bool NotifyWebhook { get; set; }
    public bool NotifyEmail { get; set; }
    public bool NotifyNtfy { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? SilencedUntilUtc { get; set; }
}
