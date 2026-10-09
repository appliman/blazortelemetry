namespace BlazorTelemetry.Core;

public sealed class TelemetryUser
{
    public long Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string ProtectedSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ActivatedUtc { get; set; }
    public Guid SecurityVersion { get; set; } = Guid.NewGuid();
    public long LastAcceptedInterval { get; set; } = -1;
}
