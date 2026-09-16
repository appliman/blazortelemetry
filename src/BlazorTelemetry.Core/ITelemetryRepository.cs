namespace BlazorTelemetry.Core;

public interface ITelemetryRepository
{
    Task Store(IReadOnlyCollection<TelemetryItem> items, CancellationToken cancellationToken);
    Task<TelemetryPage> Query(TelemetryQuery query, CancellationToken cancellationToken);
    Task<TelemetrySummary> GetSummary(DateTimeOffset fromUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(string name, string? serviceName, DateTimeOffset fromUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertRule>> GetAlertRules(CancellationToken cancellationToken);
    Task SaveAlertRule(AlertRule rule, CancellationToken cancellationToken);
    Task DeleteAlertRule(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Incident>> GetIncidents(bool includeResolved, CancellationToken cancellationToken);
    Task AcknowledgeIncident(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardDefinition>> GetDashboards(CancellationToken cancellationToken);
    Task SaveDashboard(DashboardDefinition dashboard, CancellationToken cancellationToken);
    Task<IReadOnlyList<IngestionApplication>> GetIngestionApplications(CancellationToken cancellationToken);
    Task<IngestionApplication?> FindActiveIngestionApplication(string keyHash, CancellationToken cancellationToken);
    Task SaveIngestionApplication(IngestionApplication application, CancellationToken cancellationToken);
    Task<int> Purge(DateTimeOffset rawBeforeUtc, DateTimeOffset metricsBeforeUtc, long maximumBytes, CancellationToken cancellationToken);
}
