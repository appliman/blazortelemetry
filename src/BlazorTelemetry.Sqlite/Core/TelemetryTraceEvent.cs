namespace BlazorTelemetry.Core;

public sealed class TelemetryTraceEvent
{
    public long TraceRecordId { get; set; }
    public int Ordinal { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public string AttributesJson { get; set; } = "{}";
}
