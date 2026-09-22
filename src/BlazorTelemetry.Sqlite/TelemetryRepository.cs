using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BlazorTelemetry.Sqlite;

public sealed class TelemetryRepository(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    BlazorTelemetryOptions? options = null) : ITelemetryRepository
{
    private static readonly string[] BLAZOR_METRIC_NAMES =
    [
        "aspnetcore.components.navigation",
        "aspnetcore.components.navigate",
        "aspnetcore.components.event_handler",
        "aspnetcore.components.handle_event.duration",
        "aspnetcore.components.update_parameters",
        "aspnetcore.components.update_parameters.duration",
        "aspnetcore.components.render_diff",
        "aspnetcore.components.render_diff.duration",
        "aspnetcore.components.circuit.active",
        "aspnetcore.components.circuit.connected",
        "aspnetcore.components.circuit.duration"
    ];

    private static readonly string[] NAVIGATION_METRIC_NAMES = ["aspnetcore.components.navigation", "aspnetcore.components.navigate"];
    private static readonly string[] EVENT_HANDLER_METRIC_NAMES = ["aspnetcore.components.event_handler", "aspnetcore.components.handle_event.duration"];
    private static readonly string[] UPDATE_PARAMETERS_METRIC_NAMES = ["aspnetcore.components.update_parameters", "aspnetcore.components.update_parameters.duration"];
    private static readonly string[] RENDER_DIFF_METRIC_NAMES = ["aspnetcore.components.render_diff", "aspnetcore.components.render_diff.duration"];

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

        if (!string.IsNullOrWhiteSpace(query.ExcludedServiceName))
        {
            source = source.Where(item => item.ServiceName != query.ExcludedServiceName);
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

        if (query.MinimumStatusCode.HasValue)
        {
            var minimumStatusCode = query.MinimumStatusCode.Value;
            source = source.Where(item => item.StatusCode >= minimumStatusCode);
        }

        if (query.MaximumStatusCode.HasValue)
        {
            var maximumStatusCode = query.MaximumStatusCode.Value;
            source = source.Where(item => item.StatusCode <= maximumStatusCode);
        }

        if (query.HasStatusCode.HasValue)
        {
            var hasStatusCode = query.HasStatusCode.Value;
            source = source.Where(item => item.StatusCode.HasValue == hasStatusCode);
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

    public async Task<IReadOnlyList<string>> GetServices(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await _context.TelemetryItems.AsNoTracking()
            .Where(_item => _item.TimestampUtc >= fromUtc && _item.TimestampUtc <= toUtc)
            .Select(_item => _item.ServiceName).Distinct().OrderBy(_name => _name).ToListAsync(cancellationToken);
    }

    public async Task<TelemetrySummary> GetSummary(DateTimeOffset fromUtc, CancellationToken cancellationToken, DateTimeOffset? toUtc = null, string? serviceName = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking().Where(item => item.TimestampUtc >= fromUtc);
        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(_item => _item.ServiceName == serviceName);
        }
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

    public async Task<DashboardBreakdown> GetDashboardBreakdown(
        DateTimeOffset fromUtc,
        string? serviceName,
        string? excludedRequestServiceName,
        CancellationToken cancellationToken,
        DateTimeOffset? toUtc = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Request && item.TimestampUtc >= fromUtc);
        var entityFrameworkMetrics = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric
                && item.Name == "blazortelemetry.entity_framework.commands"
                && item.NumericValue.HasValue);

        if (toUtc.HasValue)
        {
            requests = requests.Where(_item => _item.TimestampUtc <= toUtc.Value);
            entityFrameworkMetrics = entityFrameworkMetrics.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            requests = requests.Where(item => item.ServiceName == serviceName);
            entityFrameworkMetrics = entityFrameworkMetrics.Where(item => item.ServiceName == serviceName);
        }

        if (!string.IsNullOrWhiteSpace(excludedRequestServiceName))
        {
            requests = requests.Where(item => item.ServiceName != excludedRequestServiceName);
        }

        var methodRows = await requests
            .Where(item => item.Body != null && item.Body != string.Empty)
            .GroupBy(item => item.Body!)
            .Select(group => new { Method = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var statusRows = await requests
            .Where(item => item.StatusCode.HasValue)
            .GroupBy(item => item.StatusCode!.Value)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var _baselines = await entityFrameworkMetrics.Where(_item => _item.TimestampUtc < fromUtc)
            .GroupBy(item => new { item.ServiceName, item.ResourceAttributesJson, item.AttributesJson })
            .Select(_group => _group.OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id).First())
            .ToListAsync(cancellationToken);
        var _previous = _baselines.GroupBy(MetricCounter.SeriesKey).ToDictionary(_group => _group.Key, _group => _group.MaxBy(_item => _item.TimestampUtc)!);
        var operations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await foreach (var _item in entityFrameworkMetrics.Where(_item => _item.TimestampUtc >= fromUtc)
            .OrderBy(_item => _item.TimestampUtc).ThenBy(_item => _item.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var _key = MetricCounter.SeriesKey(_item);
            var _increment = MetricCounter.Increment(_item, _previous.GetValueOrDefault(_key), fromUtc);
            var _operation = ReadMetricAttribute(_item.AttributesJson, "db.operation.type");
            if (_operation is not null)
            {
                operations.TryAdd(_operation, 0);
                if (_increment.HasValue)
                {
                    operations[_operation] += Convert.ToInt64(_increment.Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            _previous[_key] = _item;
        }

        var methods = methodRows
            .GroupBy(item => item.Method.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count), StringComparer.OrdinalIgnoreCase);
        var statuses = statusRows.ToDictionary(item => item.Status, item => item.Count);

        return new DashboardBreakdown(methods, statuses, operations);
    }

    public async Task<IReadOnlyList<TelemetryItem>> GetResourceMetrics(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? serviceName, CancellationToken cancellationToken)
    {
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var _source = _context.TelemetryItems.AsNoTracking().Where(_item => _item.Kind == TelemetryKind.Metric
            && _item.TimestampUtc <= toUtc && _item.NumericValue.HasValue);
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            _source = _source.Where(_item => _item.ServiceName == serviceName);
        }

        var _result = new List<TelemetryItem>();
        // Each instrument has its own budget; busy EF/HTTP instruments cannot evict process metrics.
        var _maximumRows = Math.Max(2, options?.MaximumDashboardMetricRows ?? 5_000);
        foreach (var _name in ResourceMetricNames.All)
        {
            var _instrument = _source.Where(_item => _item.Name == _name);
            var _points = await _instrument.Where(_item => _item.TimestampUtc >= fromUtc)
                .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                .Take(_maximumRows).ToListAsync(cancellationToken);
            if (_points.Count == 0)
            {
                continue;
            }
            var _firstTimestamp = _points.Min(_item => _item.TimestampUtc);
            var _latestTimestamps = _instrument.Where(_item => _item.TimestampUtc < _firstTimestamp)
                .GroupBy(_item => new { _item.ServiceName, _item.ResourceAttributesJson, _item.AttributesJson })
                .Select(_group => new { _group.Key.ServiceName, _group.Key.ResourceAttributesJson, _group.Key.AttributesJson, TimestampUtc = _group.Max(_item => _item.TimestampUtc) });
            var _baselineQuery = from _item in _instrument
                                 join _latest in _latestTimestamps
                                 on new { _item.ServiceName, _item.ResourceAttributesJson, _item.AttributesJson, _item.TimestampUtc }
                                 equals new { _latest.ServiceName, _latest.ResourceAttributesJson, _latest.AttributesJson, _latest.TimestampUtc }
                                 select _item;
            var _baselines = await _baselineQuery
                .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
                .Take(_maximumRows).ToListAsync(cancellationToken);
            var _series = _points.Select(MetricCounter.SeriesKey).ToHashSet();
            _result.AddRange(_baselines.Where(_item => _series.Contains(MetricCounter.SeriesKey(_item))));
            _result.AddRange(_points);
        }
        return _result;
    }

    public async Task<BlazorDashboardMetrics> GetBlazorDashboardMetrics(
        DateTimeOffset fromUtc,
        string? serviceName,
        CancellationToken cancellationToken,
        DateTimeOffset? toUtc = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric
                && item.TimestampUtc >= fromUtc
                && BLAZOR_METRIC_NAMES.Contains(item.Name));

        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        var maximumRows = Math.Max(1, options?.MaximumDashboardMetricRows ?? 5_000);
        var metrics = await source
            .OrderByDescending(item => item.TimestampUtc)
            .Take(maximumRows)
            .Select(item => new TelemetryItem
            {
                TimestampUtc = item.TimestampUtc,
                ServiceName = item.ServiceName,
                Name = item.Name,
                Unit = item.Unit,
                MetricType = item.MetricType,
                NumericValue = item.NumericValue,
                ResourceAttributesJson = item.ResourceAttributesJson,
                AttributesJson = item.AttributesJson,
                DetailsJson = item.DetailsJson
            })
            .ToListAsync(cancellationToken);
        metrics.Reverse();
        if (metrics.Count == 0)
        {
            return BlazorDashboardMetrics.Empty;
        }

        var activeCircuitMetrics = SelectMetrics(metrics, "aspnetcore.components.circuit.active");
        var connectedCircuitMetrics = SelectMetrics(metrics, "aspnetcore.components.circuit.connected");
        var navigationMetrics = SelectMetrics(metrics, NAVIGATION_METRIC_NAMES);
        var eventHandlerMetrics = SelectMetrics(metrics, EVENT_HANDLER_METRIC_NAMES);
        var updateParametersMetrics = SelectMetrics(metrics, UPDATE_PARAMETERS_METRIC_NAMES);
        var renderDiffMetrics = SelectMetrics(metrics, RENDER_DIFF_METRIC_NAMES);
        var circuitDurationMetrics = SelectMetrics(metrics, "aspnetcore.components.circuit.duration");

        var routes = navigationMetrics
            .GroupBy(item => new
            {
                Route = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.route") ?? "Unknown route",
                Component = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.type") ?? "Unknown component"
            })
            .Select(group => new BlazorRouteMetric(
                group.Key.Route,
                group.Key.Component,
                Convert.ToInt64(GetCounterDelta(group), System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToInt64(GetMetricEventCount(group.Where(HasError)), System.Globalization.CultureInfo.InvariantCulture)))
            .Where(item => item.Navigations > 0 || item.Errors > 0)
            .OrderByDescending(item => item.Navigations)
            .ThenBy(item => item.Route)
            .Take(10)
            .ToArray();

        var handlers = eventHandlerMetrics
            .GroupBy(item => new
            {
                Component = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.type") ?? "Unknown component",
                Method = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.method", "code.function.name") ?? "Unknown method",
                EventName = ReadMetricAttribute(item.AttributesJson, "aspnetcore.components.attribute.name") ?? "event"
            })
            .Select(group =>
            {
                var histogram = AggregateHistogram(group);
                return new BlazorHandlerMetric(
                    group.Key.Component,
                    group.Key.Method,
                    group.Key.EventName,
                    histogram.Count,
                    histogram.Count == 0 ? 0 : histogram.Sum / histogram.Count,
                    histogram.P95,
                    Convert.ToInt64(GetMetricEventCount(group.Where(HasError)), System.Globalization.CultureInfo.InvariantCulture));
            })
            .Where(item => item.Invocations > 0)
            .OrderByDescending(item => item.P95DurationMs)
            .ThenByDescending(item => item.Invocations)
            .Take(10)
            .ToArray();

        var circuitDuration = AggregateHistogram(circuitDurationMetrics);
        var eventHandlerDuration = AggregateHistogram(eventHandlerMetrics);
        var updateParametersDuration = AggregateHistogram(updateParametersMetrics);
        var renderDiffDuration = AggregateHistogram(renderDiffMetrics);

        return new BlazorDashboardMetrics(
            GetLatestGaugeValue(activeCircuitMetrics),
            GetLatestGaugeValue(connectedCircuitMetrics),
            Convert.ToInt64(GetCounterDelta(navigationMetrics), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt64(GetMetricEventCount(metrics.Where(HasError)), System.Globalization.CultureInfo.InvariantCulture),
            circuitDuration.Count == 0 ? null : circuitDuration.P95,
            eventHandlerDuration.Count == 0 ? null : eventHandlerDuration.P95,
            updateParametersDuration.Count == 0 ? null : updateParametersDuration.P95,
            renderDiffDuration.Count == 0 ? null : renderDiffDuration.P95,
            BuildGaugeSeries(activeCircuitMetrics),
            BuildGaugeSeries(connectedCircuitMetrics),
            routes,
            handlers);
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

    public async Task<IReadOnlyList<MetricSeriesPoint>> GetMetricSeries(string name, string? serviceName, DateTimeOffset fromUtc, CancellationToken cancellationToken, DateTimeOffset? toUtc = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric && item.Name == name && item.TimestampUtc >= fromUtc && item.NumericValue.HasValue);
        if (toUtc.HasValue)
        {
            source = source.Where(_item => _item.TimestampUtc <= toUtc.Value);
        }
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
        while (await GetUsedDatabaseBytes(connection, cancellationToken) > maximumBytes)
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

        await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);", cancellationToken);

        return purged;
    }

    private static IReadOnlyList<TelemetryItem> SelectMetrics(IEnumerable<TelemetryItem> metrics, params string[] names)
    {
        return metrics.Where(item => names.Contains(item.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static long GetLatestGaugeValue(IEnumerable<TelemetryItem> metrics)
    {
        var value = metrics
            .Where(item => item.NumericValue.HasValue)
            .GroupBy(MetricSeriesKey)
            .Sum(group => Math.Max(0, group.OrderBy(item => item.TimestampUtc).Last().NumericValue!.Value));
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<MetricSeriesPoint> BuildGaugeSeries(IEnumerable<TelemetryItem> metrics)
    {
        return metrics
            .Where(item => item.NumericValue.HasValue)
            .GroupBy(item => ToTenSecondBucket(item.TimestampUtc))
            .OrderBy(group => group.Key)
            .Select(group => new MetricSeriesPoint(
                group.Key,
                group.GroupBy(MetricSeriesKey)
                    .Sum(series => Math.Max(0, series.OrderBy(item => item.TimestampUtc).Last().NumericValue!.Value))))
            .TakeLast(40)
            .ToArray();
    }

    private static double GetMetricEventCount(IEnumerable<TelemetryItem> metrics)
    {
        var materialized = metrics.ToArray();
        var histogramMetrics = materialized.Where(IsHistogram).ToArray();
        var counterMetrics = materialized.Where(item => !IsHistogram(item)).ToArray();
        return AggregateHistogram(histogramMetrics).Count + GetCounterDelta(counterMetrics);
    }

    private static double GetCounterDelta(IEnumerable<TelemetryItem> metrics)
    {
        var total = 0d;
        foreach (var series in metrics.Where(item => item.NumericValue.HasValue).GroupBy(MetricSeriesKey))
        {
            var values = series.OrderBy(item => item.TimestampUtc).Select(item => item.NumericValue!.Value).ToArray();
            if (values.Length == 1)
            {
                total += Math.Max(0, values[0]);
                continue;
            }

            for (var index = 1; index < values.Length; index++)
            {
                var delta = values[index] - values[index - 1];
                total += delta >= 0 ? delta : Math.Max(0, values[index]);
            }
        }

        return total;
    }

    private static (long Count, double Sum, double P95) AggregateHistogram(IEnumerable<TelemetryItem> metrics)
    {
        var snapshots = metrics.Select(ReadHistogramSnapshot).Where(snapshot => snapshot.HasValue).Select(snapshot => snapshot!.Value).ToArray();
        if (snapshots.Length == 0)
        {
            return (0, 0, 0);
        }

        double[]? referenceBounds = null;
        var aggregateBuckets = Array.Empty<long>();
        var totalSum = 0d;
        double? overflowMaximum = null;

        foreach (var series in snapshots.GroupBy(snapshot => snapshot.SeriesKey))
        {
            var ordered = series.OrderBy(snapshot => snapshot.TimestampUtc).ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var currentBounds = ordered[^1].Bounds;
            if (referenceBounds is null)
            {
                referenceBounds = currentBounds;
                aggregateBuckets = new long[ordered[^1].Buckets.Length];
            }

            if (!referenceBounds.SequenceEqual(currentBounds) || aggregateBuckets.Length != ordered[^1].Buckets.Length)
            {
                continue;
            }

            if (ordered.Length == 1)
            {
                AddBuckets(aggregateBuckets, ordered[0].Buckets);
                totalSum += Math.Max(0, ordered[0].Sum);
                overflowMaximum = Max(overflowMaximum, ordered[0].Maximum);
                continue;
            }

            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                var deltaBuckets = new long[current.Buckets.Length];
                for (var bucketIndex = 0; bucketIndex < current.Buckets.Length; bucketIndex++)
                {
                    var delta = current.Buckets[bucketIndex] - previous.Buckets[bucketIndex];
                    deltaBuckets[bucketIndex] = delta >= 0 ? delta : current.Buckets[bucketIndex];
                }

                AddBuckets(aggregateBuckets, deltaBuckets);
                var sumDelta = current.Sum - previous.Sum;
                totalSum += sumDelta >= 0 ? sumDelta : Math.Max(0, current.Sum);
                overflowMaximum = Max(overflowMaximum, current.Maximum);
            }
        }

        var count = aggregateBuckets.Sum();
        if (count == 0 || referenceBounds is null)
        {
            return (0, 0, 0);
        }

        var target = (long)Math.Ceiling(count * 0.95d);
        var cumulative = 0L;
        for (var index = 0; index < aggregateBuckets.Length; index++)
        {
            cumulative += aggregateBuckets[index];
            if (cumulative < target)
            {
                continue;
            }

            var percentile = index < referenceBounds.Length
                ? referenceBounds[index]
                : overflowMaximum ?? referenceBounds.LastOrDefault();
            return (count, totalSum, percentile);
        }

        return (count, totalSum, overflowMaximum ?? referenceBounds.LastOrDefault());
    }

    private static (DateTimeOffset TimestampUtc, string SeriesKey, double[] Bounds, long[] Buckets, double Sum, double? Maximum)? ReadHistogramSnapshot(TelemetryItem item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.DetailsJson);
            if (!TryGetProperty(document.RootElement, "data", out var data)
                || !TryGetProperty(data, "bounds", out var boundsElement)
                || !TryGetProperty(data, "buckets", out var bucketsElement))
            {
                return null;
            }

            var multiplier = DurationToMillisecondsMultiplier(item.Unit);
            var bounds = boundsElement.EnumerateArray().Select(value => value.GetDouble() * multiplier).ToArray();
            var buckets = bucketsElement.EnumerateArray().Select(value => checked((long)value.GetUInt64())).ToArray();
            var sum = TryGetProperty(data, "sum", out var sumElement) && sumElement.ValueKind == JsonValueKind.Number
                ? sumElement.GetDouble() * multiplier
                : 0;
            var maximum = TryGetProperty(data, "max", out var maxElement) && maxElement.ValueKind == JsonValueKind.Number
                ? maxElement.GetDouble() * multiplier
                : (double?)null;
            return (item.TimestampUtc, MetricSeriesKey(item), bounds, buckets, sum, maximum);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static void AddBuckets(long[] target, IReadOnlyList<long> values)
    {
        for (var index = 0; index < target.Length; index++)
        {
            target[index] += values[index];
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool HasError(TelemetryItem item)
    {
        return !string.IsNullOrWhiteSpace(ReadMetricAttribute(item.AttributesJson, "error.type"));
    }

    private static bool IsHistogram(TelemetryItem item)
    {
        return item.MetricType?.Contains("histogram", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string MetricSeriesKey(TelemetryItem item)
    {
        return $"{item.Name}\u001f{item.ServiceName}\u001f{item.ResourceAttributesJson}\u001f{item.AttributesJson}";
    }

    private static DateTimeOffset ToTenSecondBucket(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var bucketTicks = TimeSpan.TicksPerSecond * 10;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % bucketTicks, TimeSpan.Zero);
    }

    private static double DurationToMillisecondsMultiplier(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "s" => 1_000d,
        "us" or "µs" => 0.001d,
        "ns" => 0.000001d,
        _ => 1d
    };

    private static double? Max(double? left, double? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return Math.Max(left.Value, right.Value);
    }

    private static async Task<long> GetUsedDatabaseBytes(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        var pageCount = await ReadPragma("page_count", connection, cancellationToken);
        var freePageCount = await ReadPragma("freelist_count", connection, cancellationToken);
        var pageSize = await ReadPragma("page_size", connection, cancellationToken);
        return Math.Max(0, pageCount - freePageCount) * pageSize;
    }

    private static async Task<long> ReadPragma(
        string pragma,
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadMetricAttribute(string json, params string[] names)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var name in names)
            {
                if (document.RootElement.TryGetProperty(name, out var value))
                {
                    return value.ToString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
