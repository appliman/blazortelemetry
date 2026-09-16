namespace BlazorTelemetry.Host.Data;

public sealed class BootstrapAdminOptions
{
    public const string SECTION_NAME = "BootstrapAdmin";
    public string? Email { get; set; }
    public string? Password { get; set; }
}
