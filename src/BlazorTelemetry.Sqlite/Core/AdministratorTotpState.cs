namespace BlazorTelemetry.Core;

public sealed class AdministratorTotpState
{
    public string Identifier { get; set; } = string.Empty;
    public long LastAcceptedInterval { get; set; } = -1;
    public string SecretVersion { get; set; } = string.Empty;
}
