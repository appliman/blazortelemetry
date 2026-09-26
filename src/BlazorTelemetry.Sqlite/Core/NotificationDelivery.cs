namespace BlazorTelemetry.Core;

public sealed class NotificationDelivery
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public string? LastError { get; set; }
}
