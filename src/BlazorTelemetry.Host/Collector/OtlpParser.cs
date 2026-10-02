using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorTelemetry.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;

namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpParser(OtlpValueConverter valueConverter)
{
    private const int MAXIMUM_ITEMS_PER_REQUEST = 5_000;

    public IReadOnlyList<TelemetryItem> ParseLogs(ExportLogsServiceRequest request, string? fallbackService)
    {
        var result = new List<TelemetryItem>();
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resource = valueConverter.ToDictionary(resourceLogs.Resource?.Attributes ?? []);
            var resourceJson = JsonSerializer.Serialize(resource);
            var service = GetString(resource, "service.name") ?? fallbackService ?? "unknown-service";
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                foreach (var record in scopeLogs.LogRecords)
                {
                    var timestamp = FromUnixNano(record.TimeUnixNano != 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano);
                    AddItem(result, new TelemetryItem
                    {
                        Kind = TelemetryKind.Log,
                        TimestampUtc = timestamp,
                        ObservedUtc = FromUnixNano(record.ObservedTimeUnixNano != 0 ? record.ObservedTimeUnixNano : record.TimeUnixNano),
                        ServiceName = service,
                        ServiceVersion = GetString(resource, "service.version"),
                        Environment = GetString(resource, "deployment.environment.name") ?? GetString(resource, "deployment.environment"),
                        Name = string.IsNullOrWhiteSpace(record.EventName) ? scopeLogs.Scope?.Name ?? "log" : record.EventName,
                        Body = ValueToText(record.Body),
                        SeverityText = record.SeverityText,
                        SeverityNumber = (int)record.SeverityNumber,
                        TraceId = ToHex(record.TraceId),
                        SpanId = ToHex(record.SpanId),
                        ResourceAttributesJson = resourceJson,
                        AttributesJson = valueConverter.ToJson(record.Attributes),
                        ScopeName = scopeLogs.Scope?.Name,
                        ScopeVersion = scopeLogs.Scope?.Version,
                        Flags = record.Flags,
                        DroppedAttributesCount = record.DroppedAttributesCount
                    });
                }
            }
        }

        return result;
    }

    public IReadOnlyList<TelemetryItem> ParseTraces(ExportTraceServiceRequest request, string? fallbackService)
    {
        var result = new List<TelemetryItem>();
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var resource = valueConverter.ToDictionary(resourceSpans.Resource?.Attributes ?? []);
            var resourceJson = JsonSerializer.Serialize(resource);
            var service = GetString(resource, "service.name") ?? fallbackService ?? "unknown-service";
            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                foreach (var span in scopeSpans.Spans)
                {
                    var traceId = ToHex(span.TraceId);
                    var spanId = ToHex(span.SpanId);
                    var duration = span.EndTimeUnixNano >= span.StartTimeUnixNano
                        ? (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1_000_000d
                        : 0;
                    var attributes = valueConverter.ToDictionary(span.Attributes);
                    var attributesJson = JsonSerializer.Serialize(attributes);
                    AddItem(result, new TelemetryItem
                    {
                        Kind = TelemetryKind.Trace,
                        TimestampUtc = FromUnixNano(span.StartTimeUnixNano),
                        ObservedUtc = DateTimeOffset.UtcNow,
                        ServiceName = service,
                        ServiceVersion = GetString(resource, "service.version"),
                        Environment = GetString(resource, "deployment.environment.name") ?? GetString(resource, "deployment.environment"),
                        Name = span.Name,
                        TraceId = traceId,
                        SpanId = spanId,
                        ParentSpanId = ToHex(span.ParentSpanId),
                        DurationMs = duration,
                        StatusCode = (int)(span.Status?.Code ?? 0),
                        Body = span.Status?.Message,
                        ResourceAttributesJson = resourceJson,
                        AttributesJson = attributesJson,
                        ScopeName = scopeSpans.Scope?.Name,
                        ScopeVersion = scopeSpans.Scope?.Version,
                        SpanKind = span.Kind.ToString(),
                        Events = span.Events.Take(valueConverter.MaximumCollectionItems).Select((_event, _ordinal) => new TelemetryTraceEvent
                        {
                            Ordinal = _ordinal,
                            Name = _event.Name,
                            TimestampUtc = FromUnixNano(_event.TimeUnixNano),
                            AttributesJson = valueConverter.ToJson(_event.Attributes)
                        }).ToList(),
                        Links = span.Links.Take(valueConverter.MaximumCollectionItems).Select((_link, _ordinal) => new TelemetryTraceLink
                        {
                            Ordinal = _ordinal,
                            TraceId = ToHex(_link.TraceId),
                            SpanId = ToHex(_link.SpanId),
                            AttributesJson = valueConverter.ToJson(_link.Attributes)
                        }).ToList(),
                        Fingerprint = Hash($"trace:{traceId}:{spanId}")
                    });

                    if (span.Kind == OpenTelemetry.Proto.Trace.V1.Span.Types.SpanKind.Server)
                    {
                        AddItem(result, new TelemetryItem
                        {
                            Kind = TelemetryKind.Request,
                            TimestampUtc = FromUnixNano(span.StartTimeUnixNano),
                            ObservedUtc = DateTimeOffset.UtcNow,
                            ServiceName = service,
                            ServiceVersion = GetString(resource, "service.version"),
                            Environment = GetString(resource, "deployment.environment.name") ?? GetString(resource, "deployment.environment"),
                            Name = RequestName(span.Name, attributes),
                            Body = GetString(attributes, "http.request.method") ?? GetString(attributes, "http.method") ?? "—",
                            TraceId = traceId,
                            SpanId = spanId,
                            ParentSpanId = ToHex(span.ParentSpanId),
                            DurationMs = duration,
                            StatusCode = GetInt(attributes, "http.response.status_code") ?? GetInt(attributes, "http.status_code"),
                            ResourceAttributesJson = resourceJson,
                            AttributesJson = attributesJson,
                            ScopeName = scopeSpans.Scope?.Name,
                            ScopeVersion = scopeSpans.Scope?.Version,
                            SpanKind = span.Kind.ToString(),
                            Source = "OTLP server span",
                            Fingerprint = Hash($"request:{traceId}:{spanId}")
                        });
                    }
                }
            }
        }

        return result;
    }

    public IReadOnlyList<TelemetryItem> ParseMetrics(ExportMetricsServiceRequest request, string? fallbackService)
    {
        var result = new List<TelemetryItem>();
        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            var resource = valueConverter.ToDictionary(resourceMetrics.Resource?.Attributes ?? []);
            var resourceJson = JsonSerializer.Serialize(resource);
            var service = GetString(resource, "service.name") ?? fallbackService ?? "unknown-service";
            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                foreach (var metric in scopeMetrics.Metrics)
                {
                    ParseMetric(result, metric, service, resource, resourceJson, scopeMetrics.Scope?.Name, scopeMetrics.Scope?.Version);
                }
            }
        }

        return result;
    }

    private void ParseMetric(List<TelemetryItem> _result, Metric _metric, string _service,
        Dictionary<string, object?> _resource, string _resourceJson, string? _scope, string? _scopeVersion)
    {
        switch (_metric.DataCase)
        {
            case Metric.DataOneofCase.Gauge:
            {
                foreach (var _point in _metric.Gauge.DataPoints)
                {
                    var _item = CreateMetric(_metric, "gauge", _service, _resource, _resourceJson, _scope, _scopeVersion, _point.TimeUnixNano, _point.Attributes);
                    _item.NumericValue = Number(_point);
                    _item.StartTimeUtc = StartTime(_point.StartTimeUnixNano);
                    _item.Exemplars = Exemplars(_point.Exemplars);
                    AddMetric(_result, _item);
                }
                break;
            }
            case Metric.DataOneofCase.Sum:
            {
                foreach (var _point in _metric.Sum.DataPoints)
                {
                    var _item = CreateMetric(_metric, "sum", _service, _resource, _resourceJson, _scope, _scopeVersion, _point.TimeUnixNano, _point.Attributes);
                    _item.NumericValue = Number(_point);
                    _item.StartTimeUtc = StartTime(_point.StartTimeUnixNano);
                    _item.AggregationTemporality = _metric.Sum.AggregationTemporality.ToString();
                    _item.IsMonotonic = _metric.Sum.IsMonotonic;
                    _item.Exemplars = Exemplars(_point.Exemplars);
                    AddMetric(_result, _item);
                }
                break;
            }
            case Metric.DataOneofCase.Histogram:
            {
                foreach (var _point in _metric.Histogram.DataPoints)
                {
                    var _item = CreateMetric(_metric, "histogram", _service, _resource, _resourceJson, _scope, _scopeVersion, _point.TimeUnixNano, _point.Attributes);
                    _item.StartTimeUtc = StartTime(_point.StartTimeUnixNano);
                    _item.Count = checked((long)_point.Count);
                    _item.Sum = _point.HasSum ? _point.Sum : null;
                    _item.Minimum = _point.HasMin ? _point.Min : null;
                    _item.Maximum = _point.HasMax ? _point.Max : null;
                    _item.NumericValue = _item.Sum ?? _item.Count;
                    _item.AggregationTemporality = _metric.Histogram.AggregationTemporality.ToString();
                    _item.Buckets = _point.BucketCounts.Take(valueConverter.MaximumCollectionItems).Select((_count, _ordinal) => new TelemetryMetricBucket
                    {
                        Ordinal = _ordinal,
                        Count = checked((long)_count),
                        UpperBound = _ordinal < _point.ExplicitBounds.Count ? _point.ExplicitBounds[_ordinal] : null
                    }).ToList();
                    _item.Exemplars = Exemplars(_point.Exemplars);
                    AddMetric(_result, _item);
                }
                break;
            }
            case Metric.DataOneofCase.ExponentialHistogram:
            {
                foreach (var _point in _metric.ExponentialHistogram.DataPoints)
                {
                    var _item = CreateMetric(_metric, "exponential-histogram", _service, _resource, _resourceJson, _scope, _scopeVersion, _point.TimeUnixNano, _point.Attributes);
                    _item.StartTimeUtc = StartTime(_point.StartTimeUnixNano);
                    _item.Count = checked((long)_point.Count);
                    _item.Sum = _point.HasSum ? _point.Sum : null;
                    _item.Minimum = _point.HasMin ? _point.Min : null;
                    _item.Maximum = _point.HasMax ? _point.Max : null;
                    _item.NumericValue = _item.Sum ?? _item.Count;
                    _item.Scale = _point.Scale;
                    _item.ZeroCount = checked((long)_point.ZeroCount);
                    _item.PositiveOffset = _point.Positive?.Offset;
                    _item.NegativeOffset = _point.Negative?.Offset;
                    _item.AggregationTemporality = _metric.ExponentialHistogram.AggregationTemporality.ToString();
                    _item.Buckets = ExponentialBuckets(_point.Positive, 1).Concat(ExponentialBuckets(_point.Negative, -1)).ToList();
                    _item.Exemplars = Exemplars(_point.Exemplars);
                    AddMetric(_result, _item);
                }
                break;
            }
            case Metric.DataOneofCase.Summary:
            {
                foreach (var _point in _metric.Summary.DataPoints)
                {
                    var _item = CreateMetric(_metric, "summary", _service, _resource, _resourceJson, _scope, _scopeVersion, _point.TimeUnixNano, _point.Attributes);
                    _item.StartTimeUtc = StartTime(_point.StartTimeUnixNano);
                    _item.Count = checked((long)_point.Count);
                    _item.Sum = _point.Sum;
                    _item.NumericValue = _point.Sum;
                    _item.Quantiles = _point.QuantileValues.Take(valueConverter.MaximumCollectionItems).Select((_value, _ordinal) => new TelemetryMetricQuantile
                    {
                        Ordinal = _ordinal,
                        Quantile = _value.Quantile,
                        Value = _value.Value
                    }).ToList();
                    AddMetric(_result, _item);
                }
                break;
            }
        }
    }

    private TelemetryItem CreateMetric(Metric _metric, string _type, string _service, Dictionary<string, object?> _resource,
        string _resourceJson, string? _scope, string? _scopeVersion, ulong _timestamp, IEnumerable<KeyValue> _attributes)
    {
        return new TelemetryItem
        {
            Kind = TelemetryKind.Metric,
            TimestampUtc = FromUnixNano(_timestamp),
            ObservedUtc = DateTimeOffset.UtcNow,
            ServiceName = _service,
            ServiceVersion = GetString(_resource, "service.version"),
            Environment = GetString(_resource, "deployment.environment.name") ?? GetString(_resource, "deployment.environment"),
            Name = _metric.Name,
            Body = _metric.Description,
            Unit = _metric.Unit,
            MetricType = _type,
            ScopeName = _scope,
            ScopeVersion = _scopeVersion,
            ResourceAttributesJson = _resourceJson,
            AttributesJson = valueConverter.ToJson(_attributes),
            Fingerprint = _timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private List<TelemetryMetricExemplar> Exemplars(IEnumerable<Exemplar> _exemplars)
    {
        return _exemplars.Take(valueConverter.MaximumCollectionItems).Select((_exemplar, _ordinal) => new TelemetryMetricExemplar
        {
            Ordinal = _ordinal,
            TimestampUtc = FromUnixNano(_exemplar.TimeUnixNano),
            TraceId = ToHex(_exemplar.TraceId),
            SpanId = ToHex(_exemplar.SpanId),
            Value = _exemplar.ValueCase == Exemplar.ValueOneofCase.AsDouble ? _exemplar.AsDouble : ExactInteger(_exemplar.AsInt),
            AttributesJson = valueConverter.ToJson(_exemplar.FilteredAttributes)
        }).ToList();
    }

    private IEnumerable<TelemetryMetricBucket> ExponentialBuckets(ExponentialHistogramDataPoint.Types.Buckets? _buckets, int _group)
    {
        return _buckets?.BucketCounts.Take(valueConverter.MaximumCollectionItems).Select((_count, _ordinal) => new TelemetryMetricBucket
        {
            Group = _group,
            Ordinal = _ordinal,
            Count = checked((long)_count)
        }) ?? [];
    }

    private static DateTimeOffset? StartTime(ulong _timestamp) => _timestamp == 0 ? null : FromUnixNano(_timestamp);

    private static double Number(NumberDataPoint _point) => _point.ValueCase == NumberDataPoint.ValueOneofCase.AsDouble ? _point.AsDouble : ExactInteger(_point.AsInt);

    private static double ExactInteger(long _value)
    {
        var _converted = (double)_value;
        if (new System.Numerics.BigInteger(_converted) != _value)
        {
            throw new OverflowException("OTLP integer cannot be represented safely as a metric value.");
        }
        return _converted;
    }

    private static void AddMetric(List<TelemetryItem> _result, TelemetryItem _item)
    {
        _item.Fingerprint = Hash(FormattableString.Invariant($"metric:{_item.ServiceName}:{TelemetryNormalization.Canonical(_item.ResourceAttributesJson)}:{_item.ScopeName}:{_item.Name}:{_item.Fingerprint}:{TelemetryNormalization.Canonical(_item.AttributesJson)}:{_item.NumericValue}"));
        AddItem(_result, _item);
    }

    private string? ValueToText(AnyValue? value)
    {
        var converted = valueConverter.ToObject(value);
        return converted switch
        {
            null => null,
            string text => text,
            _ => JsonSerializer.Serialize(converted)
        };
    }

    private static DateTimeOffset FromUnixNano(ulong value)
    {
        if (value == 0)
        {
            return DateTimeOffset.UtcNow;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)(value / 1_000_000));
    }

    private static string? ToHex(Google.Protobuf.ByteString bytes)
    {
        return bytes.IsEmpty ? null : Convert.ToHexString(bytes.Span).ToLowerInvariant();
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> source, string key)
    {
        return source.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            _ when int.TryParse(value.ToString(), out var number) => number,
            _ => null
        };
    }

    private static string RequestName(string spanName, IReadOnlyDictionary<string, object?> attributes)
    {
        var fullUrl = GetString(attributes, "url.full") ?? GetString(attributes, "http.url");
        if (!string.IsNullOrWhiteSpace(fullUrl))
        {
            return fullUrl;
        }

        var path = GetString(attributes, "url.path")
            ?? GetString(attributes, "http.target")
            ?? GetString(attributes, "http.route");
        var host = GetString(attributes, "server.address") ?? GetString(attributes, "http.host");
        if (string.IsNullOrWhiteSpace(host))
        {
            return path ?? spanName;
        }

        var scheme = GetString(attributes, "url.scheme") ?? GetString(attributes, "http.scheme") ?? "http";
        var port = GetInt(attributes, "server.port");
        var portPart = port.HasValue ? $":{port.Value}" : string.Empty;
        return $"{scheme}://{host}{portPart}{path}";
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static void AddItem(List<TelemetryItem> result, TelemetryItem item)
    {
        if (result.Count >= MAXIMUM_ITEMS_PER_REQUEST)
        {
            throw new OtlpBatchTooLargeException(MAXIMUM_ITEMS_PER_REQUEST);
        }

        TelemetryNormalization.Validate(item);
        TelemetryCollectionLimits.Apply(item);
        result.Add(item);
    }

}
