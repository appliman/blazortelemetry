using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;

namespace BlazorTelemetry.Host.Handlers.Metrics;

internal static class MetricQueryHelpers
{
    internal static readonly Expression<Func<TelemetryItem, TelemetryItem>> METRIC_PROJECTION = _item => new TelemetryItem
    {
        CollectionsTruncated = _item.CollectionsTruncated,
        Id = _item.Id,
        Kind = _item.Kind,
        ResourceId = _item.ResourceId,
        TimestampUtc = _item.TimestampUtc,
        ServiceName = _item.ServiceName,
        ServiceVersion = _item.ServiceVersion,
        ServiceInstanceId = _item.ServiceInstanceId,
        Environment = _item.Environment,
        Name = _item.Name,
        Unit = _item.Unit,
        MetricType = _item.MetricType,
        NumericValue = _item.NumericValue,
        ResourceAttributesJson = _item.ResourceAttributesJson,
        AttributesJson = _item.AttributesJson,
        ScopeName = _item.ScopeName,
        ScopeVersion = _item.ScopeVersion,
        StartTimeUtc = _item.StartTimeUtc,
        AggregationTemporality = _item.AggregationTemporality,
        IsMonotonic = _item.IsMonotonic,
        Count = _item.Count,
        Sum = _item.Sum,
        Minimum = _item.Minimum,
        Maximum = _item.Maximum,
        Scale = _item.Scale,
        ZeroCount = _item.ZeroCount,
        PositiveOffset = _item.PositiveOffset,
        NegativeOffset = _item.NegativeOffset
    };

    internal static readonly string[] BLAZOR_METRIC_NAMES =
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

    internal static readonly string[] NAVIGATION_METRIC_NAMES = ["aspnetcore.components.navigation", "aspnetcore.components.navigate"];
    internal static readonly string[] EVENT_HANDLER_METRIC_NAMES = ["aspnetcore.components.event_handler", "aspnetcore.components.handle_event.duration"];
    internal static readonly string[] UPDATE_PARAMETERS_METRIC_NAMES = ["aspnetcore.components.update_parameters", "aspnetcore.components.update_parameters.duration"];
    internal static readonly string[] RENDER_DIFF_METRIC_NAMES = ["aspnetcore.components.render_diff", "aspnetcore.components.render_diff.duration"];

    internal static IReadOnlyList<TelemetryItem> SelectMetrics(IEnumerable<TelemetryItem> metrics, params string[] names)
    {
        return metrics.Where(item => names.Contains(item.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    internal static long GetLatestGaugeValue(IEnumerable<TelemetryItem> metrics)
    {
        var value = metrics
            .Where(item => item.NumericValue.HasValue)
            .GroupBy(MetricSeriesKey)
            .Sum(group => Math.Max(0, group.OrderBy(item => item.TimestampUtc).Last().NumericValue!.Value));
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static IReadOnlyList<MetricSeriesPoint> BuildGaugeSeries(IEnumerable<TelemetryItem> metrics)
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

    internal static double GetMetricEventCount(IEnumerable<TelemetryItem> metrics)
    {
        var materialized = metrics.ToArray();
        var histogramMetrics = materialized.Where(IsHistogram).ToArray();
        var counterMetrics = materialized.Where(item => !IsHistogram(item)).ToArray();
        return AggregateHistogram(histogramMetrics).Count + GetCounterDelta(counterMetrics);
    }

    internal static double GetCounterDelta(IEnumerable<TelemetryItem> metrics)
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

    internal static (long Count, double Sum, double P95) AggregateHistogram(IEnumerable<TelemetryItem> metrics)
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

    internal static (DateTimeOffset TimestampUtc, string SeriesKey, double[] Bounds, long[] Buckets, double Sum, double? Maximum)? ReadHistogramSnapshot(TelemetryItem item)
    {
        if (item.MetricType != "histogram" || item.Buckets.Count == 0)
        {
            return null;
        }
        var multiplier = DurationToMillisecondsMultiplier(item.Unit);
        var ordered = item.Buckets.Where(_bucket => _bucket.Group == 0).OrderBy(_bucket => _bucket.Ordinal).ToArray();
        var bounds = ordered.Where(_bucket => _bucket.UpperBound.HasValue).Select(_bucket => _bucket.UpperBound!.Value * multiplier).ToArray();
        var buckets = ordered.Select(_bucket => _bucket.Count).ToArray();
        return (item.TimestampUtc, MetricSeriesKey(item), bounds, buckets, (item.Sum ?? 0) * multiplier, item.Maximum * multiplier);
    }

    internal static void AddBuckets(long[] target, IReadOnlyList<long> values)
    {
        for (var index = 0; index < target.Length; index++)
        {
            target[index] += values[index];
        }
    }

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
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

    internal static bool HasError(TelemetryItem item)
    {
        return !string.IsNullOrWhiteSpace(ReadMetricAttribute(item.AttributesJson, "error.type"));
    }

    internal static bool IsHistogram(TelemetryItem item)
    {
        return item.MetricType?.Contains("histogram", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static string MetricSeriesKey(TelemetryItem item)
    {
        return $"{item.Name}\u001f{item.ServiceName}\u001f{(item.ResourceId == 0 ? item.ResourceAttributesJson : item.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture))}\u001f{item.ScopeName}\u001f{item.AttributesJson}";
    }

    internal static DateTimeOffset ToTenSecondBucket(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var bucketTicks = TimeSpan.TicksPerSecond * 10;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % bucketTicks, TimeSpan.Zero);
    }

    internal static double DurationToMillisecondsMultiplier(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "s" => 1_000d,
        "us" or "µs" => 0.001d,
        "ns" => 0.000001d,
        _ => 1d
    };

    internal static double? Max(double? left, double? right)
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

    internal static async Task<long> GetUsedDatabaseBytes(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        var pageCount = await ReadPragma("page_count", connection, cancellationToken);
        var freePageCount = await ReadPragma("freelist_count", connection, cancellationToken);
        var pageSize = await ReadPragma("page_size", connection, cancellationToken);
        return Math.Max(0, pageCount - freePageCount) * pageSize;
    }

    internal static async Task<long> ReadPragma(
        string pragma,
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string? ReadMetricAttribute(string json, params string[] names)
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
    internal static IQueryable<TelemetryItem> MetricBaselines(IQueryable<TelemetryItem> _items, IQueryable<TelemetryItem> _source, IQueryable<TelemetryItem> _points, DateTimeOffset _beforeUtc)
    {
        var _series = _points.Select(_item => new { _item.ServiceName, _item.ResourceId, _item.AttributesJson, _item.ScopeName }).Distinct();
        // A scalar indexed lookup per active series, instead of sorting all retained
        // metric payloads with ROW_NUMBER or grouping the complete history.
        var _ids = _series.Select(_seriesItem => _source
            .Where(_item => _item.ServiceName == _seriesItem.ServiceName
                && _item.ResourceId == _seriesItem.ResourceId
                && _item.ScopeName == _seriesItem.ScopeName
                && _item.AttributesJson == _seriesItem.AttributesJson
                && _item.TimestampUtc < _beforeUtc)
            .OrderByDescending(_item => _item.TimestampUtc).ThenByDescending(_item => _item.Id)
            .Select(_item => (long?)_item.Id).FirstOrDefault());
        return _items.AsNoTracking().Where(_item => _ids.Contains(_item.Id));
    }

}
