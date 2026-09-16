namespace BlazorTelemetry.Core;

public sealed class Incident
{
    public long Id { get; set; }
    public int AlertRuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public IncidentState State { get; set; }
    public string Severity { get; set; } = "warning";
    public double ObservedValue { get; set; }
    public double Threshold { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset LastEvaluatedUtc { get; set; }
    public DateTimeOffset? ResolvedUtc { get; set; }
    public DateTimeOffset? AcknowledgedUtc { get; set; }
}
