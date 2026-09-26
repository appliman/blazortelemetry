using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorTelemetry.Core;
using Microsoft.Data.Sqlite;

namespace BlazorTelemetry.Host.Configuration;

public sealed class TelemetryOptionsOverrideStore
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly TelemetryOptionsOverrides? _startupOverrides;

    public TelemetryOptionsOverrideStore(string connectionString)
    {
        var databasePath = Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);
        _path = Path.Combine(Path.GetDirectoryName(databasePath)!, "blazor-telemetry-options.json");
        _startupOverrides = Read();
        if (_startupOverrides is not null && Validate(_startupOverrides).Count > 0)
        {
            throw new InvalidDataException("The saved telemetry options are invalid.");
        }
    }

    public bool HasOverrides => File.Exists(_path);

    public TelemetryOptionsOverrides? GetForEditing()
    {
        var saved = Read();
        if (saved is null)
        {
            return null;
        }

        saved.Webhook.BearerToken = null;
        saved.Smtp.Password = null;
        saved.Ntfy.AccessToken = null;
        return saved;
    }

    public void Apply(BlazorTelemetryOptions options)
    {
        if (_startupOverrides is not { } saved)
        {
            return;
        }

        options.RawRetentionDays = saved.RawRetentionDays;
        options.MetricRetentionDays = saved.MetricRetentionDays;
        options.MaximumDatabaseBytes = saved.MaximumDatabaseBytes;
        options.QueueCapacity = saved.QueueCapacity;
        options.IngestionAcknowledgementTimeoutSeconds = saved.IngestionAcknowledgementTimeoutSeconds;
        options.MaximumRequestBytes = saved.MaximumRequestBytes;
        options.MaximumAttributes = saved.MaximumAttributes;
        options.MaximumQueryRows = saved.MaximumQueryRows;
        options.MaximumDashboardMetricRows = saved.MaximumDashboardMetricRows;
        options.DashboardRefreshIntervalSeconds = saved.DashboardRefreshIntervalSeconds;
        options.DbContextPoolSize = saved.DbContextPoolSize;
        options.EvaluationIntervalSeconds = saved.EvaluationIntervalSeconds;
        options.RequireIngestionKey = saved.RequireIngestionKey;
        options.SensitiveAttributePatterns = [.. saved.SensitiveAttributePatterns];
        options.Webhook = new WebhookOptions
        {
            Url = saved.Webhook.Url,
            BearerToken = saved.Webhook.BearerToken ?? options.Webhook.BearerToken
        };
        options.Smtp = new SmtpOptions
        {
            Host = saved.Smtp.Host,
            Port = saved.Smtp.Port,
            UseTls = saved.Smtp.UseTls,
            UserName = saved.Smtp.UserName,
            Password = saved.Smtp.Password ?? options.Smtp.Password,
            From = saved.Smtp.From,
            Recipients = [.. saved.Smtp.Recipients]
        };
        options.Ntfy = new NtfyOptions
        {
            ServerUrl = saved.Ntfy.ServerUrl,
            Topic = saved.Ntfy.Topic,
            AccessToken = saved.Ntfy.AccessToken ?? options.Ntfy.AccessToken
        };
    }

    public async Task Save(
        TelemetryOptionsOverrides values,
        string? webhookToken,
        bool clearWebhookToken,
        string? smtpPassword,
        bool clearSmtpPassword,
        string? ntfyToken,
        bool clearNtfyToken,
        CancellationToken cancellationToken)
    {
        var errors = Validate(values);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(values));
        }

        var previous = Read();
        var persisted = JsonSerializer.Deserialize<TelemetryOptionsOverrides>(
            JsonSerializer.Serialize(values, JSON_OPTIONS), JSON_OPTIONS)!;
        persisted.Webhook.BearerToken = SecretValue(webhookToken, clearWebhookToken, previous?.Webhook.BearerToken);
        persisted.Smtp.Password = SecretValue(smtpPassword, clearSmtpPassword, previous?.Smtp.Password);
        persisted.Ntfy.AccessToken = SecretValue(ntfyToken, clearNtfyToken, previous?.Ntfy.AccessToken);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(persisted, JSON_OPTIONS), cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Reset()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    public static IReadOnlyList<string> Validate(TelemetryOptionsOverrides values)
    {
        var errors = new List<string>();
        if (values.RawRetentionDays <= 0 || values.MetricRetentionDays <= 0)
        {
            errors.Add("Retention periods must be greater than zero.");
        }
        if (values.MaximumDatabaseBytes <= 0 || values.MaximumRequestBytes <= 0)
        {
            errors.Add("Database and request size limits must be greater than zero.");
        }
        if (values.QueueCapacity <= 0 || values.IngestionAcknowledgementTimeoutSeconds <= 0 ||
            values.MaximumAttributes <= 0 || values.MaximumQueryRows <= 0 ||
            values.MaximumDashboardMetricRows <= 0 || values.DashboardRefreshIntervalSeconds <= 0 ||
            values.DbContextPoolSize <= 0 || values.EvaluationIntervalSeconds <= 0)
        {
            errors.Add("Capacities, row limits, and intervals must be greater than zero.");
        }
        if (values.Webhook is null || values.Smtp is null || values.Ntfy is null ||
            values.SensitiveAttributePatterns is null || values.Smtp?.Recipients is null)
        {
            errors.Add("The saved option groups are incomplete.");
            return errors;
        }
        if (values.Smtp.Port is < 1 or > 65535)
        {
            errors.Add("SMTP port must be between 1 and 65535.");
        }
        if (!string.IsNullOrWhiteSpace(values.Webhook.Url) && !IsHttpUrl(values.Webhook.Url))
        {
            errors.Add("Webhook URL must start with http:// or https://.");
        }
        if (!IsHttpUrl(values.Ntfy.ServerUrl))
        {
            errors.Add("ntfy server URL must start with http:// or https://.");
        }
        return errors;
    }

    private TelemetryOptionsOverrides? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        return JsonSerializer.Deserialize<TelemetryOptionsOverrides>(File.ReadAllText(_path), JSON_OPTIONS)
            ?? throw new InvalidDataException("The saved telemetry options file is empty.");
    }

    private static string? SecretValue(string? entered, bool clear, string? previous)
    {
        if (clear)
        {
            return string.Empty;
        }
        return string.IsNullOrWhiteSpace(entered) ? previous : entered;
    }

    private static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
