namespace BlazorTelemetry.Core;

public sealed class TelemetryResource
{
    public long Id { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceVersion { get; set; }
    public string? Environment { get; set; }
    public string? ServiceInstanceId { get; set; }
    public string? SdkName { get; set; }
    public string? SdkLanguage { get; set; }
    public string? SdkVersion { get; set; }
    public string AttributesJson { get; set; } = "{}";
}
