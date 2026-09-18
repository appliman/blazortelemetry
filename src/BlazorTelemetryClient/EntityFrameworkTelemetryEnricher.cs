using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Data;
using Microsoft.Extensions.Hosting;

namespace BlazorTelemetry.Client;

public sealed class EntityFrameworkTelemetryEnricher : IHostedService, IDisposable
{
    public const string METER_NAME = "BlazorTelemetry.EntityFrameworkCore";
    public const string COMMANDS_METRIC_NAME = "blazortelemetry.entity_framework.commands";
    public const string FAILURES_METRIC_NAME = "blazortelemetry.entity_framework.failures";
    public const string ACTIVE_COMMANDS_METRIC_NAME = "blazortelemetry.entity_framework.active_commands";
    public const string DURATION_METRIC_NAME = "blazortelemetry.entity_framework.command.duration";

    private const string ACTIVITY_SOURCE_NAME = "OpenTelemetry.Instrumentation.EntityFrameworkCore";
    private readonly Meter _meter = new(METER_NAME, "1.0.0");
    private readonly Counter<long> _commands;
    private readonly Counter<long> _failures;
    private readonly UpDownCounter<long> _activeCommands;
    private readonly Histogram<double> _duration;
    private ActivityListener? _listener;

    public EntityFrameworkTelemetryEnricher()
    {
        _commands = _meter.CreateCounter<long>(COMMANDS_METRIC_NAME, "{command}", "Number of completed Entity Framework database commands.");
        _failures = _meter.CreateCounter<long>(FAILURES_METRIC_NAME, "{command}", "Number of failed Entity Framework database commands.");
        _activeCommands = _meter.CreateUpDownCounter<long>(ACTIVE_COMMANDS_METRIC_NAME, "{command}", "Number of Entity Framework database commands currently executing.");
        _duration = _meter.CreateHistogram<double>(DURATION_METRIC_NAME, "ms", "Entity Framework database command execution duration.");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, ACTIVITY_SOURCE_NAME, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => _activeCommands.Add(1),
            ActivityStopped = RecordCompletedCommand
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    internal static void Enrich(Activity activity, IDbCommand command)
    {
        activity.SetTag("db.operation.type", GetOperationType(command.CommandText));
        activity.SetTag("db.command.type", command.CommandType.ToString().ToLowerInvariant());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _meter.Dispose();
    }

    private void RecordCompletedCommand(Activity activity)
    {
        _activeCommands.Add(-1);

        var tags = CreateTags(activity);
        _commands.Add(1, tags);
        _duration.Record(activity.Duration.TotalMilliseconds, tags);

        if (activity.Status == ActivityStatusCode.Error || GetTag(activity, "error.type") is not null)
        {
            var errorTags = new TagList
            {
                { "db.system.name", GetTag(activity, "db.system.name") ?? GetTag(activity, "db.system") },
                { "db.operation.name", GetOperation(activity) },
                { "db.operation.type", GetOperationType(activity) },
                { "error.type", GetTag(activity, "error.type") ?? "unknown" }
            };
            _failures.Add(1, errorTags);
        }
    }

    private static TagList CreateTags(Activity activity)
    {
        return new TagList
        {
            { "db.system.name", GetTag(activity, "db.system.name") ?? GetTag(activity, "db.system") },
            { "db.operation.name", GetOperation(activity) },
            { "db.operation.type", GetOperationType(activity) }
        };
    }

    private static string GetOperationType(Activity activity)
    {
        return GetTag(activity, "db.operation.type") ?? GetOperationType(GetOperation(activity));
    }

    private static string GetOperationType(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return "other";
        }

        var normalized = operation.TrimStart();
        var separatorIndex = normalized.IndexOfAny([' ', '\t', '\r', '\n', '(', ';']);
        var keyword = separatorIndex >= 0 ? normalized[..separatorIndex] : normalized;
        return keyword.ToUpperInvariant() switch
        {
            "SELECT" or "READ" => "read",
            "INSERT" or "CREATE" => "insert",
            "UPDATE" or "UPSERT" or "MERGE" => "update",
            "DELETE" or "REMOVE" => "delete",
            _ => "other"
        };
    }

    private static string GetOperation(Activity activity)
    {
        return GetTag(activity, "db.operation.name")
            ?? GetTag(activity, "db.operation")
            ?? "unknown";
    }

    private static string? GetTag(Activity activity, string name)
    {
        return activity.GetTagItem(name)?.ToString();
    }
}
