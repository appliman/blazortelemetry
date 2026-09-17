using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Sqlite;

public sealed class TelemetryRepository(IDbContextFactory<TelemetryDbContext> contextFactory) : ITelemetryRepository
{
    public async Task CompleteRequest(string requestId, double durationMs, int statusCode, CancellationToken cancellationToken)
    {
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await _context.TelemetryItems.Where(_item => _item.Kind == TelemetryKind.Request && _item.TraceId == requestId)
            .ExecuteUpdateAsync(_setters => _setters.SetProperty(_item => _item.DurationMs, durationMs)
                .SetProperty(_item => _item.StatusCode, statusCode), cancellationToken);
    }

    public async Task<IReadOnlyList<IngestionApplication>> GetIngestionApplications(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.IngestionApplications.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
    }

    public async Task<IngestionApplication?> FindActiveIngestionApplication(string keyHash, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var application = await context.IngestionApplications.FirstOrDefaultAsync(item => item.IsActive && item.KeyHash == keyHash, cancellationToken);
        if (application is null)
        {
            return null;
        }

        application.LastSeenUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return application;
    }

    public async Task SaveIngestionApplication(IngestionApplication application, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (application.Id == 0)
        {
            await context.IngestionApplications.AddAsync(application, cancellationToken);
        }
        else
        {
            context.IngestionApplications.Update(application);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task Store(IReadOnlyCollection<TelemetryItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (var chunk in items.Chunk(500))
        {
            var uniqueItems = chunk
                .Where(item => item.Fingerprint is null)
                .Concat(chunk
                    .Where(item => item.Fingerprint is not null)
                    .DistinctBy(item => item.Fingerprint, StringComparer.Ordinal))
                .ToArray();
            var fingerprints = uniqueItems
                .Where(item => item.Fingerprint is not null)
                .Select(item => item.Fingerprint!)
                .ToArray();
            HashSet<string> existingFingerprints = [];
            if (fingerprints.Length > 0)
            {
                existingFingerprints = (await context.TelemetryItems
                    .AsNoTracking()
                    .Where(item => item.Fingerprint != null && fingerprints.Contains(item.Fingerprint))
                    .Select(item => item.Fingerprint!)
                    .ToListAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);
            }

            var newItems = uniqueItems
                .Where(item => item.Fingerprint is null || !existingFingerprints.Contains(item.Fingerprint))
                .ToArray();
            await context.TelemetryItems.AddRangeAsync(newItems, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            context.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TelemetryPage> Query(TelemetryQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, 500);
        var skip = Math.Max(0, query.Skip);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking().AsQueryable();

        if (query.Kind.HasValue)
        {
            source = source.Where(item => item.Kind == query.Kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ServiceName))
        {
            source = source.Where(item => item.ServiceName == query.ServiceName);
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            source = source.Where(item => item.TraceId == query.TraceId);
        }

        if (query.MinimumSeverityNumber.HasValue)
        {
            var minimumSeverityNumber = query.MinimumSeverityNumber.Value;
            source = source.Where(item => item.SeverityNumber >= minimumSeverityNumber);
        }

        if (query.MaximumSeverityNumber.HasValue)
        {
            var maximumSeverityNumber = query.MaximumSeverityNumber.Value;
            source = source.Where(item => item.SeverityNumber <= maximumSeverityNumber);
        }

        if (query.FromUtc.HasValue)
        {
            var fromUtc = query.FromUtc.Value;
            source = source.Where(item => item.TimestampUtc >= fromUtc);
        }

        if (query.ToUtc.HasValue)
        {
            var toUtc = query.ToUtc.Value;
            source = source.Where(item => item.TimestampUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.Name.Contains(search) || (item.Body != null && item.Body.Contains(search)) || item.AttributesJson.Contains(search));
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(item => item.TimestampUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new TelemetryPage(items, total, total > skip + items.Count);
    }

    public async Task<TelemetrySummary> GetSummary(DateTimeOffset fromUtc, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking().Where(item => item.TimestampUtc >= fromUtc);
        var logs = await source.LongCountAsync(item => item.Kind == TelemetryKind.Log, cancellationToken);
        var traces = await source.LongCountAsync(item => item.Kind == TelemetryKind.Trace, cancellationToken);
        var metrics = await source.LongCountAsync(item => item.Kind == TelemetryKind.Metric, cancellationToken);
        var errors = await source.LongCountAsync(item => item.StatusCode == 2 || (item.SeverityNumber ?? 0) >= 17, cancellationToken);
        var durations = await source.Where(item => item.Kind == TelemetryKind.Trace && item.DurationMs.HasValue)
            .OrderBy(item => item.DurationMs)
            .Select(item => item.DurationMs!.Value)
            .Take(10_000)
            .ToListAsync(cancellationToken);
        var p95 = durations.Count == 0 ? 0 : durations[(int)Math.Floor((durations.Count - 1) * 0.95)];
        var activeIncidents = await context.Incidents.CountAsync(incident => incident.State != IncidentState.Resolved, cancellationToken);
        var oldest = await source.OrderBy(item => item.TimestampUtc).Select(item => (DateTimeOffset?)item.TimestampUtc).FirstOrDefaultAsync(cancellationToken);
        var newest = await source.OrderByDescending(item => item.TimestampUtc).Select(item => (DateTimeOffset?)item.TimestampUtc).FirstOrDefaultAsync(cancellationToken);
        var services = await source.Select(item => item.ServiceName).Distinct().OrderBy(name => name).Take(100).ToListAsync(cancellationToken);
        return new(logs, traces, metrics, errors, p95, activeIncidents, oldest, newest, services);
    }

    public async Task<IReadOnlyDictionary<string, long>> GetErrorCountsByService(
        DateTimeOffset fromUtc,
        string? serviceName,
        string? search,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Log && item.TimestampUtc >= fromUtc && (item.SeverityNumber ?? 0) >= 17);

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            source = source.Where(item => item.Name.Contains(normalizedSearch)
                || (item.Body != null && item.Body.Contains(normalizedSearch))
                || item.AttributesJson.Contains(normalizedSearch));
        }

        var counts = await source
            .GroupBy(item => item.ServiceName)
            .Select(group => new { ServiceName = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(item => item.ServiceName, item => item.Count, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(string name, string? serviceName, DateTimeOffset fromUtc, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric && item.Name == name && item.TimestampUtc >= fromUtc && item.NumericValue.HasValue);
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        var values = await source.OrderBy(item => item.TimestampUtc).Take(2_000)
            .Select(item => new { item.TimestampUtc, Value = item.NumericValue!.Value })
            .ToListAsync(cancellationToken);
        return values.Select(value => new MetricSeriesPoint(value.TimestampUtc, value.Value)).ToList();
    }

    public async Task<IReadOnlyList<AlertRule>> GetAlertRules(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AlertRules.AsNoTracking().OrderBy(rule => rule.Name).ToListAsync(cancellationToken);
    }

    public async Task SaveAlertRule(AlertRule rule, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Update(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAlertRule(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.AlertRules.Where(rule => rule.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Incident>> GetIncidents(bool includeResolved, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.Incidents.AsNoTracking().AsQueryable();
        if (!includeResolved)
        {
            source = source.Where(incident => incident.State != IncidentState.Resolved);
        }

        return await source.OrderByDescending(incident => incident.StartedUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task AcknowledgeIncident(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await context.Incidents.Where(incident => incident.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(incident => incident.AcknowledgedUtc, now), cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardDefinition>> GetDashboards(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Dashboards.AsNoTracking().OrderBy(dashboard => dashboard.Name).ToListAsync(cancellationToken);
    }

    public async Task SaveDashboard(DashboardDefinition dashboard, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        dashboard.UpdatedUtc = DateTimeOffset.UtcNow;
        context.Update(dashboard);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> Purge(DateTimeOffset rawBeforeUtc, DateTimeOffset metricsBeforeUtc, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var purged = await context.TelemetryItems.Where(item => item.Kind != TelemetryKind.Metric && item.TimestampUtc < rawBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        purged += await context.TelemetryItems.Where(item => item.Kind == TelemetryKind.Metric && item.TimestampUtc < metricsBeforeUtc).ExecuteDeleteAsync(cancellationToken);

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        while (await GetLogicalTelemetryBytes(connection, cancellationToken) > maximumBytes)
        {
            var removed = await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM TelemetryItems WHERE Id IN (SELECT Id FROM TelemetryItems ORDER BY TimestampUtc LIMIT 10000)",
                cancellationToken);
            if (removed == 0)
            {
                break;
            }

            purged += removed;
        }

        if (purged > 0)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);", cancellationToken);
        }

        return purged;
    }

    private static async Task<long> GetLogicalTelemetryBytes(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(
                256 + length(COALESCE(AttributesJson, '')) + length(COALESCE(Body, '')) +
                length(COALESCE(DetailsJson, '')) + length(COALESCE(ResourceAttributesJson, '')) +
                length(COALESCE(Name, '')) + length(COALESCE(ServiceName, ''))
            ), 0)
            FROM TelemetryItems;
            """;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
