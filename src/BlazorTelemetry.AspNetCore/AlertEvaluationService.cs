using System.Text.Json;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.AspNetCore;

internal sealed class AlertEvaluationService(
    IServiceScopeFactory scopeFactory,
    BlazorTelemetryOptions options,
    ILogger<AlertEvaluationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.EvaluationIntervalSeconds)));
        do
        {
            try
            {
                await Evaluate(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Alert evaluation failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task Evaluate(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        var rules = await repository.GetAlertRules(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var rule in rules.Where(rule => rule.IsEnabled && (!rule.SilencedUntilUtc.HasValue || rule.SilencedUntilUtc <= now)))
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var activeIncidents = await context.Incidents
                .Where(item => item.AlertRuleId == rule.Id && item.State != IncidentState.Resolved)
                .ToListAsync(cancellationToken);
            var observedValues = await GetObservedValues(repository, rule, now, cancellationToken);
            var services = observedValues.Keys
                .Concat(activeIncidents.Select(incident => incident.ServiceName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var service in services)
            {
                var value = observedValues.GetValueOrDefault(service);
                var exceeds = rule.Type == AlertRuleType.TelemetryAbsence ? value >= rule.Threshold : value > rule.Threshold;
                var incident = activeIncidents.FirstOrDefault(item => item.ServiceName == service);

                if (exceeds)
                {
                    if (incident is null)
                    {
                        incident = new Incident
                        {
                            AlertRuleId = rule.Id,
                            RuleName = rule.Name,
                            ServiceName = service,
                            Severity = rule.Severity,
                            State = rule.ConfirmationMinutes == 0 ? IncidentState.Firing : IncidentState.Pending,
                            ObservedValue = value,
                            Threshold = rule.Threshold,
                            StartedUtc = now,
                            LastEvaluatedUtc = now
                        };
                        context.Incidents.Add(incident);
                        await context.SaveChangesAsync(cancellationToken);
                        if (incident.State == IncidentState.Firing)
                        {
                            QueueNotifications(context, rule, incident, "firing", now);
                        }
                    }
                    else
                    {
                        incident.ObservedValue = value;
                        incident.LastEvaluatedUtc = now;
                        if (incident.State == IncidentState.Pending && now - incident.StartedUtc >= TimeSpan.FromMinutes(rule.ConfirmationMinutes))
                        {
                            incident.State = IncidentState.Firing;
                            QueueNotifications(context, rule, incident, "firing", now);
                        }
                    }
                }
                else if (incident is not null)
                {
                    var wasFiring = incident.State == IncidentState.Firing;
                    incident.State = IncidentState.Resolved;
                    incident.ResolvedUtc = now;
                    incident.LastEvaluatedUtc = now;
                    if (wasFiring)
                    {
                        QueueNotifications(context, rule, incident, "resolved", now);
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyDictionary<string, double>> GetObservedValues(
        ITelemetryRepository repository,
        AlertRule rule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fromUtc = now.AddMinutes(-Math.Max(1, rule.WindowMinutes));
        var service = rule.ServiceName == "*" ? null : rule.ServiceName;
        if (rule.Type == AlertRuleType.ErrorLogs)
        {
            var counts = await repository.GetErrorCountsByService(fromUtc, service, rule.Query, cancellationToken);
            if (service is null)
            {
                return counts.ToDictionary(item => item.Key, item => (double)item.Value, StringComparer.Ordinal);
            }

            return new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [service] = counts.GetValueOrDefault(service)
            };
        }

        if (rule.Type == AlertRuleType.MetricThreshold)
        {
            var series = await repository.GetMetricSeries(rule.Query ?? string.Empty, service, fromUtc, cancellationToken);
            return new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [rule.ServiceName] = series.Count == 0 ? 0 : series.Max(point => point.Value)
            };
        }

        var page = await repository.Query(new TelemetryQuery(
            Kind: rule.Type == AlertRuleType.ErrorLogs ? TelemetryKind.Log : rule.Type is AlertRuleType.TraceErrorRate or AlertRuleType.TraceLatency ? TelemetryKind.Trace : null,
            ServiceName: service,
            Search: rule.Query,
            FromUtc: fromUtc,
            Take: 500), cancellationToken);

        var value = rule.Type switch
        {
            AlertRuleType.TraceErrorRate => page.Items.Count == 0 ? 0 : page.Items.Count(item => item.StatusCode == 2) * 100d / page.Items.Count,
            AlertRuleType.TraceLatency => Percentile95(page.Items.Where(item => item.DurationMs.HasValue).Select(item => item.DurationMs!.Value).OrderBy(value => value).ToList()),
            AlertRuleType.TelemetryAbsence => page.Items.Count == 0 ? rule.WindowMinutes : Math.Max(0, (now - page.Items.Max(item => item.TimestampUtc)).TotalMinutes),
            _ => 0
        };
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [rule.ServiceName] = value
        };
    }

    private static double Percentile95(IReadOnlyList<double> values)
    {
        return values.Count == 0 ? 0 : values[(int)Math.Floor((values.Count - 1) * 0.95)];
    }

    private static void QueueNotifications(TelemetryDbContext context, AlertRule rule, Incident incident, string transition, DateTimeOffset now)
    {
        var payload = JsonSerializer.Serialize(new
        {
            incidentId = incident.Id,
            rule = rule.Name,
            service = incident.ServiceName,
            incident.Severity,
            state = transition,
            observedValue = incident.ObservedValue,
            threshold = incident.Threshold,
            timestampUtc = now
        });

        if (rule.NotifyWebhook)
        {
            AddDelivery(context, incident.Id, "webhook", transition, payload, now);
        }
        if (rule.NotifyEmail)
        {
            AddDelivery(context, incident.Id, "email", transition, payload, now);
        }
        if (rule.NotifyNtfy)
        {
            AddDelivery(context, incident.Id, "ntfy", transition, payload, now);
        }
    }

    private static void AddDelivery(TelemetryDbContext context, long incidentId, string channel, string transition, string payload, DateTimeOffset now)
    {
        context.NotificationDeliveries.Add(new NotificationDelivery
        {
            IncidentId = incidentId,
            Channel = channel,
            EventKey = $"incident:{incidentId}:{transition}:{channel}",
            PayloadJson = payload,
            NextAttemptUtc = now
        });
    }
}
