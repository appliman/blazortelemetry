namespace BlazorTelemetry.Core;

public enum AlertRuleType
{
    ErrorLogs = 1,
    TraceErrorRate = 2,
    TraceLatency = 3,
    MetricThreshold = 4,
    TelemetryAbsence = 5
}
