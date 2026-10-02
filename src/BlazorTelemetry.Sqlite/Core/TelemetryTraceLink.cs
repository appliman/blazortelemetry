namespace BlazorTelemetry.Core;

public sealed class TelemetryTraceLink
{
    public long TraceRecordId { get; set; }
    public int Ordinal { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string AttributesJson { get; set; } = "{}";
}
