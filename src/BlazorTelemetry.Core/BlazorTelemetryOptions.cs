namespace BlazorTelemetry.Core;

public sealed class BlazorTelemetryOptions
{
    public const string SECTION_NAME = "BlazorTelemetry";

    public string ConnectionString { get; set; } = "Data Source=blazor-telemetry.db";
    public int RawRetentionDays { get; set; } = 7;
    public int MetricRetentionDays { get; set; } = 30;
    public long MaximumDatabaseBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public int QueueCapacity { get; set; } = 20_000;
    public int MaximumRequestBytes { get; set; } = 8 * 1024 * 1024;
    public int MaximumAttributes { get; set; } = 128;
    public int MaximumQueryRows { get; set; } = 500;
    public int EvaluationIntervalSeconds { get; set; } = 30;
    public string ReaderPolicy { get; set; } = "BlazorTelemetryReader";
    public string AdministratorPolicy { get; set; } = "BlazorTelemetryAdministrator";
    public bool RequireIngestionKey { get; set; }
    public Dictionary<string, string> IngestionKeys { get; set; } = new(StringComparer.Ordinal);
    public string[] SensitiveAttributePatterns { get; set; } = ["password", "secret", "token", "authorization", "cookie"];
    public WebhookOptions Webhook { get; set; } = new();
    public SmtpOptions Smtp { get; set; } = new();
    public NtfyOptions Ntfy { get; set; } = new();
}
