using BlazorTelemetry.Core;

namespace BlazorTelemetry.Host.Configuration;

public sealed class TelemetryOptionsOverrides
{
    public int RawRetentionDays { get; set; }
    public int MetricRetentionDays { get; set; }
    public long MaximumDatabaseBytes { get; set; }
    public int QueueCapacity { get; set; }
    public int IngestionAcknowledgementTimeoutSeconds { get; set; }
    public int MaximumRequestBytes { get; set; }
    public int MaximumAttributes { get; set; }
    public int MaximumQueryRows { get; set; }
    public int MaximumDashboardMetricRows { get; set; }
    public int DashboardRefreshIntervalSeconds { get; set; }
    public int DbContextPoolSize { get; set; }
    public int EvaluationIntervalSeconds { get; set; }
    public bool RequireIngestionKey { get; set; }
    public string[] SensitiveAttributePatterns { get; set; } = [];
    public WebhookOptions Webhook { get; set; } = new();
    public SmtpOptions Smtp { get; set; } = new();
    public NtfyOptions Ntfy { get; set; } = new();

    public static TelemetryOptionsOverrides From(BlazorTelemetryOptions options)
    {
        return new TelemetryOptionsOverrides
        {
            RawRetentionDays = options.RawRetentionDays,
            MetricRetentionDays = options.MetricRetentionDays,
            MaximumDatabaseBytes = options.MaximumDatabaseBytes,
            QueueCapacity = options.QueueCapacity,
            IngestionAcknowledgementTimeoutSeconds = options.IngestionAcknowledgementTimeoutSeconds,
            MaximumRequestBytes = options.MaximumRequestBytes,
            MaximumAttributes = options.MaximumAttributes,
            MaximumQueryRows = options.MaximumQueryRows,
            MaximumDashboardMetricRows = options.MaximumDashboardMetricRows,
            DashboardRefreshIntervalSeconds = options.DashboardRefreshIntervalSeconds,
            DbContextPoolSize = options.DbContextPoolSize,
            EvaluationIntervalSeconds = options.EvaluationIntervalSeconds,
            RequireIngestionKey = options.RequireIngestionKey,
            SensitiveAttributePatterns = [.. options.SensitiveAttributePatterns],
            Webhook = new WebhookOptions { Url = options.Webhook.Url },
            Smtp = new SmtpOptions
            {
                Host = options.Smtp.Host,
                Port = options.Smtp.Port,
                UseTls = options.Smtp.UseTls,
                UserName = options.Smtp.UserName,
                From = options.Smtp.From,
                Recipients = [.. options.Smtp.Recipients]
            },
            Ntfy = new NtfyOptions { ServerUrl = options.Ntfy.ServerUrl, Topic = options.Ntfy.Topic }
        };
    }
}
