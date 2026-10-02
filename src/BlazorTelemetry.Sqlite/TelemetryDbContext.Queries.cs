using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Sqlite;

public sealed partial class TelemetryDbContext
{
    public IQueryable<TelemetryItem> QueryTelemetry(TelemetryKind? _kind = null)
    {
        var _log = TelemetryLogs.AsNoTracking().Select(_item => new TelemetryItem
        {
            CollectionsTruncated = default,
            Id = _item.Id,
            TimestampUtc = _item.TimestampUtc,
            ObservedUtc = _item.ObservedUtc,
            Name = _item.Name,
            ScopeName = _item.ScopeName,
            ScopeVersion = _item.ScopeVersion,
            AttributesJson = _item.AttributesJson,
            Body = _item.Body,
            SeverityText = _item.SeverityText,
            SeverityNumber = _item.SeverityNumber,
            TraceId = _item.TraceId,
            SpanId = _item.SpanId,
            Flags = _item.Flags,
            DroppedAttributesCount = _item.DroppedAttributesCount,
            ParentSpanId = (string?)null,
            DurationMs = (double?)null,
            StatusCode = (int?)null,
            SpanKind = (string?)null,
            Fingerprint = _item.Fingerprint,
            Unit = (string?)null,
            MetricType = (string?)null,
            NumericValue = (double?)null,
            StartTimeUtc = (DateTimeOffset?)null,
            AggregationTemporality = (string?)null,
            IsMonotonic = (bool?)null,
            Count = (long?)null,
            Sum = (double?)null,
            Minimum = (double?)null,
            Maximum = (double?)null,
            Scale = (int?)null,
            ZeroCount = (long?)null,
            PositiveOffset = (int?)null,
            NegativeOffset = (int?)null,
            Source = (string?)null,
            CircuitId = (string?)null,
            HttpMethod = (string?)null,
            Url = (string?)null,
            Route = (string?)null,
            ProtocolVersion = (string?)null,
            UrlScheme = (string?)null,
            ClientAddress = (string?)null,
            ClientPort = (int?)null,
            ServerAddress = (string?)null,
            ServerPort = (int?)null,
            UserAgent = (string?)null,
            Kind = TelemetryKind.Log,
            ServiceName = _item.Resource.ServiceName,
            ServiceVersion = _item.Resource.ServiceVersion,
            Environment = _item.Resource.Environment,
            ResourceAttributesJson = _item.Resource.AttributesJson,
            ResourceId = _item.ResourceId,
            ServiceInstanceId = _item.Resource.ServiceInstanceId,
            SdkName = _item.Resource.SdkName,
            SdkLanguage = _item.Resource.SdkLanguage,
            SdkVersion = _item.Resource.SdkVersion,
        });
        var _trace = TelemetryTraces.AsNoTracking().Select(_item => new TelemetryItem
        {
            CollectionsTruncated = _item.CollectionsTruncated,
            Id = _item.Id,
            TimestampUtc = _item.TimestampUtc,
            ObservedUtc = _item.ObservedUtc,
            Name = _item.Name,
            ScopeName = _item.ScopeName,
            ScopeVersion = _item.ScopeVersion,
            AttributesJson = _item.AttributesJson,
            Body = _item.Body,
            SeverityText = (string?)null,
            SeverityNumber = (int?)null,
            TraceId = _item.TraceId,
            SpanId = _item.SpanId,
            Flags = (long?)null,
            DroppedAttributesCount = (long?)null,
            ParentSpanId = _item.ParentSpanId,
            DurationMs = _item.DurationMs,
            StatusCode = _item.StatusCode,
            SpanKind = _item.SpanKind,
            Fingerprint = _item.Fingerprint,
            Unit = (string?)null,
            MetricType = (string?)null,
            NumericValue = (double?)null,
            StartTimeUtc = (DateTimeOffset?)null,
            AggregationTemporality = (string?)null,
            IsMonotonic = (bool?)null,
            Count = (long?)null,
            Sum = (double?)null,
            Minimum = (double?)null,
            Maximum = (double?)null,
            Scale = (int?)null,
            ZeroCount = (long?)null,
            PositiveOffset = (int?)null,
            NegativeOffset = (int?)null,
            Source = (string?)null,
            CircuitId = (string?)null,
            HttpMethod = (string?)null,
            Url = (string?)null,
            Route = (string?)null,
            ProtocolVersion = (string?)null,
            UrlScheme = (string?)null,
            ClientAddress = (string?)null,
            ClientPort = (int?)null,
            ServerAddress = (string?)null,
            ServerPort = (int?)null,
            UserAgent = (string?)null,
            Kind = TelemetryKind.Trace,
            ServiceName = _item.Resource.ServiceName,
            ServiceVersion = _item.Resource.ServiceVersion,
            Environment = _item.Resource.Environment,
            ResourceAttributesJson = _item.Resource.AttributesJson,
            ResourceId = _item.ResourceId,
            ServiceInstanceId = _item.Resource.ServiceInstanceId,
            SdkName = _item.Resource.SdkName,
            SdkLanguage = _item.Resource.SdkLanguage,
            SdkVersion = _item.Resource.SdkVersion,
        });
        var _metric = TelemetryMetrics.AsNoTracking().Select(_item => new TelemetryItem
        {
            CollectionsTruncated = _item.CollectionsTruncated,
            Id = _item.Id,
            TimestampUtc = _item.TimestampUtc,
            ObservedUtc = _item.ObservedUtc,
            Name = _item.Name,
            ScopeName = _item.ScopeName,
            ScopeVersion = _item.ScopeVersion,
            AttributesJson = _item.AttributesJson,
            Body = _item.Body,
            SeverityText = (string?)null,
            SeverityNumber = (int?)null,
            TraceId = (string?)null,
            SpanId = (string?)null,
            Flags = (long?)null,
            DroppedAttributesCount = (long?)null,
            ParentSpanId = (string?)null,
            DurationMs = (double?)null,
            StatusCode = (int?)null,
            SpanKind = (string?)null,
            Fingerprint = _item.Fingerprint,
            Unit = _item.Unit,
            MetricType = _item.MetricType,
            NumericValue = _item.NumericValue,
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
            NegativeOffset = _item.NegativeOffset,
            Source = (string?)null,
            CircuitId = (string?)null,
            HttpMethod = (string?)null,
            Url = (string?)null,
            Route = (string?)null,
            ProtocolVersion = (string?)null,
            UrlScheme = (string?)null,
            ClientAddress = (string?)null,
            ClientPort = (int?)null,
            ServerAddress = (string?)null,
            ServerPort = (int?)null,
            UserAgent = (string?)null,
            Kind = TelemetryKind.Metric,
            ServiceName = _item.Resource.ServiceName,
            ServiceVersion = _item.Resource.ServiceVersion,
            Environment = _item.Resource.Environment,
            ResourceAttributesJson = _item.Resource.AttributesJson,
            ResourceId = _item.ResourceId,
            ServiceInstanceId = _item.Resource.ServiceInstanceId,
            SdkName = _item.Resource.SdkName,
            SdkLanguage = _item.Resource.SdkLanguage,
            SdkVersion = _item.Resource.SdkVersion,
        });
        var _request = TelemetryRequest.AsNoTracking().Select(_item => new TelemetryItem
        {
            CollectionsTruncated = default,
            Id = _item.Id,
            TimestampUtc = _item.TimestampUtc,
            ObservedUtc = _item.ObservedUtc,
            Name = _item.Name,
            ScopeName = _item.ScopeName,
            ScopeVersion = _item.ScopeVersion,
            AttributesJson = _item.AttributesJson,
            Body = _item.HttpMethod,
            SeverityText = (string?)null,
            SeverityNumber = (int?)null,
            TraceId = _item.TraceId,
            SpanId = _item.SpanId,
            Flags = (long?)null,
            DroppedAttributesCount = (long?)null,
            ParentSpanId = _item.ParentSpanId,
            DurationMs = _item.DurationMs,
            StatusCode = _item.StatusCode,
            SpanKind = _item.SpanKind,
            Fingerprint = _item.Fingerprint,
            Unit = (string?)null,
            MetricType = (string?)null,
            NumericValue = (double?)null,
            StartTimeUtc = (DateTimeOffset?)null,
            AggregationTemporality = (string?)null,
            IsMonotonic = (bool?)null,
            Count = (long?)null,
            Sum = (double?)null,
            Minimum = (double?)null,
            Maximum = (double?)null,
            Scale = (int?)null,
            ZeroCount = (long?)null,
            PositiveOffset = (int?)null,
            NegativeOffset = (int?)null,
            Source = _item.Source,
            CircuitId = _item.CircuitId,
            HttpMethod = _item.HttpMethod,
            Url = _item.Url,
            Route = _item.Route,
            ProtocolVersion = _item.ProtocolVersion,
            UrlScheme = _item.UrlScheme,
            ClientAddress = _item.ClientAddress,
            ClientPort = _item.ClientPort,
            ServerAddress = _item.ServerAddress,
            ServerPort = _item.ServerPort,
            UserAgent = _item.UserAgent,
            Kind = TelemetryKind.Request,
            ServiceName = _item.Resource.ServiceName,
            ServiceVersion = _item.Resource.ServiceVersion,
            Environment = _item.Resource.Environment,
            ResourceAttributesJson = _item.Resource.AttributesJson,
            ResourceId = _item.ResourceId,
            ServiceInstanceId = _item.Resource.ServiceInstanceId,
            SdkName = _item.Resource.SdkName,
            SdkLanguage = _item.Resource.SdkLanguage,
            SdkVersion = _item.Resource.SdkVersion,
        });
        return _kind switch
        {
            TelemetryKind.Log => _log,
            TelemetryKind.Trace => _trace,
            TelemetryKind.Metric => _metric,
            TelemetryKind.Request => _request,
            null => _log.Concat(_trace).Concat(_metric).Concat(_request),
            _ => throw new ArgumentOutOfRangeException(nameof(_kind))
        };
    }

    public async Task LoadTelemetryChildren(IReadOnlyCollection<TelemetryItem> _items, CancellationToken _cancellationToken)
    {
        var _traces = _items.Where(_item => _item.Kind == TelemetryKind.Trace).ToDictionary(_item => _item.Id);
        foreach (var _chunk in _traces.Keys.Chunk(500))
        {
            var _events = await Set<TelemetryTraceEvent>().AsNoTracking().Where(_child => _chunk.Contains(_child.TraceRecordId)).OrderBy(_child => _child.Ordinal).ToListAsync(_cancellationToken);
            foreach (var _child in _events)
            {
                _traces[_child.TraceRecordId].Events.Add(_child);
            }
            var _links = await Set<TelemetryTraceLink>().AsNoTracking().Where(_child => _chunk.Contains(_child.TraceRecordId)).OrderBy(_child => _child.Ordinal).ToListAsync(_cancellationToken);
            foreach (var _child in _links)
            {
                _traces[_child.TraceRecordId].Links.Add(_child);
            }
        }
        var _metrics = _items.Where(_item => _item.Kind == TelemetryKind.Metric).ToDictionary(_item => _item.Id);
        foreach (var _chunk in _metrics.Keys.Chunk(500))
        {
            var _buckets = await Set<TelemetryMetricBucket>().AsNoTracking().Where(_child => _chunk.Contains(_child.MetricId)).OrderBy(_child => _child.Ordinal).ToListAsync(_cancellationToken);
            foreach (var _child in _buckets)
            {
                _metrics[_child.MetricId].Buckets.Add(_child);
            }
            var _quantiles = await Set<TelemetryMetricQuantile>().AsNoTracking().Where(_child => _chunk.Contains(_child.MetricId)).OrderBy(_child => _child.Ordinal).ToListAsync(_cancellationToken);
            foreach (var _child in _quantiles)
            {
                _metrics[_child.MetricId].Quantiles.Add(_child);
            }
            var _exemplars = await Set<TelemetryMetricExemplar>().AsNoTracking().Where(_child => _chunk.Contains(_child.MetricId)).OrderBy(_child => _child.Ordinal).ToListAsync(_cancellationToken);
            foreach (var _child in _exemplars)
            {
                _metrics[_child.MetricId].Exemplars.Add(_child);
            }
        }
    }
}
