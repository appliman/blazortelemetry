namespace BlazorTelemetry.Core;

public sealed class DashboardDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = "[]";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
