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
    public IReadOnlyList<TelemetryItem> ParseLogs(ExportLogsServiceRequest request, string? fallbackService)
    {
        var result = new List<TelemetryItem>();
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resource = valueConverter.ToDictionary(resourceLogs.Resource?.Attributes ?? []);
            var resourceJson = JsonSerializer.Serialize(resource);
            var service = GetString(resource, "service.name") ?? fallbackService ?? "service-inconnu";
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                foreach (var record in scopeLogs.LogRecords)
                {
                    var timestamp = FromUnixNano(record.TimeUnixNano != 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano);
                    result.Add(new TelemetryItem
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
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            scope = scopeLogs.Scope?.Name,
                            scopeVersion = scopeLogs.Scope?.Version,
                            flags = record.Flags,
                            droppedAttributes = record.DroppedAttributesCount
                        })
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
            var service = GetString(resource, "service.name") ?? fallbackService ?? "service-inconnu";
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
                    result.Add(new TelemetryItem
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
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            kind = span.Kind.ToString(),
                            scope = scopeSpans.Scope?.Name,
                            events = span.Events.Select(item => new
                            {
                                item.Name,
                                timestampUtc = FromUnixNano(item.TimeUnixNano),
                                attributes = valueConverter.ToDictionary(item.Attributes)
                            }),
                            links = span.Links.Select(link => new
                            {
                                traceId = ToHex(link.TraceId),
                                spanId = ToHex(link.SpanId),
                                attributes = valueConverter.ToDictionary(link.Attributes)
                            })
                        }),
                        Fingerprint = Hash($"trace:{traceId}:{spanId}")
                    });

                    if (span.Kind == OpenTelemetry.Proto.Trace.V1.Span.Types.SpanKind.Server)
                    {
                        result.Add(new TelemetryItem
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
                            DetailsJson = JsonSerializer.Serialize(new
                            {
                                kind = span.Kind.ToString(),
                                source = "OTLP server span"
                            }),
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
            var service = GetString(resource, "service.name") ?? fallbackService ?? "service-inconnu";
            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                foreach (var metric in scopeMetrics.Metrics)
                {
                    ParseMetric(result, metric, service, resource, resourceJson, scopeMetrics.Scope?.Name);
                }
            }
        }

        return result;
    }

    private void ParseMetric(
        List<TelemetryItem> result,
        Metric metric,
        string service,
        Dictionary<string, object?> resource,
        string resourceJson,
        string? scope)
    {
        switch (metric.DataCase)
        {
            case Metric.DataOneofCase.Gauge:
                foreach (var point in metric.Gauge.DataPoints)
                {
                    AddNumberPoint(result, metric, point, "gauge", service, resource, resourceJson, scope, null);
                }
                break;
            case Metric.DataOneofCase.Sum:
                foreach (var point in metric.Sum.DataPoints)
                {
                    AddNumberPoint(result, metric, point, "sum", service, resource, resourceJson, scope, new
                    {
                        aggregationTemporality = metric.Sum.AggregationTemporality.ToString(),
                        metric.Sum.IsMonotonic,
                        exemplars = SerializeExemplars(point.Exemplars)
                    });
                }
                break;
            case Metric.DataOneofCase.Histogram:
                foreach (var point in metric.Histogram.DataPoints)
                {
                    AddMetric(result, metric, "histogram", service, resource, resourceJson, scope, point.TimeUnixNano, point.Attributes,
                        point.HasSum ? point.Sum : point.Count,
                        new
                        {
                            point.Count,
                            sum = point.HasSum ? (double?)point.Sum : null,
                            min = point.HasMin ? (double?)point.Min : null,
                            max = point.HasMax ? (double?)point.Max : null,
                            bounds = point.ExplicitBounds,
                            buckets = point.BucketCounts,
                            aggregationTemporality = metric.Histogram.AggregationTemporality.ToString(),
                            exemplars = SerializeExemplars(point.Exemplars)
                        });
                }
                break;
            case Metric.DataOneofCase.ExponentialHistogram:
                foreach (var point in metric.ExponentialHistogram.DataPoints)
                {
                    AddMetric(result, metric, "exponential-histogram", service, resource, resourceJson, scope, point.TimeUnixNano, point.Attributes,
                        point.HasSum ? point.Sum : point.Count,
                        new
                        {
                            point.Count,
                            sum = point.HasSum ? (double?)point.Sum : null,
                            min = point.HasMin ? (double?)point.Min : null,
                            max = point.HasMax ? (double?)point.Max : null,
                            point.Scale,
                            point.ZeroCount,
                            positive = point.Positive,
                            negative = point.Negative,
                            aggregationTemporality = metric.ExponentialHistogram.AggregationTemporality.ToString(),
                            exemplars = SerializeExemplars(point.Exemplars)
                        });
                }
                break;
            case Metric.DataOneofCase.Summary:
                foreach (var point in metric.Summary.DataPoints)
                {
                    AddMetric(result, metric, "summary", service, resource, resourceJson, scope, point.TimeUnixNano, point.Attributes,
                        point.Sum,
                        new
                        {
                            point.Count,
                            point.Sum,
                            quantiles = point.QuantileValues.Select(value => new { value.Quantile, value.Value })
                        });
                }
                break;
        }
    }

    private void AddNumberPoint(
        List<TelemetryItem> result,
        Metric metric,
        NumberDataPoint point,
        string type,
        string service,
        Dictionary<string, object?> resource,
        string resourceJson,
        string? scope,
        object? details)
    {
        var value = point.ValueCase == NumberDataPoint.ValueOneofCase.AsDouble ? point.AsDouble : point.AsInt;
        AddMetric(result, metric, type, service, resource, resourceJson, scope, point.TimeUnixNano, point.Attributes, value,
            details ?? new { exemplars = SerializeExemplars(point.Exemplars) });
    }

    private void AddMetric(
        List<TelemetryItem> result,
        Metric metric,
        string type,
        string service,
        Dictionary<string, object?> resource,
        string resourceJson,
        string? scope,
        ulong timestampNano,
        IEnumerable<KeyValue> attributes,
        double value,
        object details)
    {
        var timestamp = FromUnixNano(timestampNano);
        var attributesJson = valueConverter.ToJson(attributes);
        result.Add(new TelemetryItem
        {
            Kind = TelemetryKind.Metric,
            TimestampUtc = timestamp,
            ObservedUtc = DateTimeOffset.UtcNow,
            ServiceName = service,
            ServiceVersion = GetString(resource, "service.version"),
            Environment = GetString(resource, "deployment.environment.name") ?? GetString(resource, "deployment.environment"),
            Name = metric.Name,
            Body = metric.Description,
            Unit = metric.Unit,
            MetricType = type,
            NumericValue = value,
            ResourceAttributesJson = resourceJson,
            AttributesJson = attributesJson,
            DetailsJson = JsonSerializer.Serialize(new { scope, data = details }),
            Fingerprint = Hash($"metric:{service}:{metric.Name}:{timestampNano}:{attributesJson}:{value}")
        });
    }

    private object SerializeExemplars(IEnumerable<Exemplar> exemplars)
    {
        return exemplars.Select(exemplar => new
        {
            timestampUtc = FromUnixNano(exemplar.TimeUnixNano),
            traceId = ToHex(exemplar.TraceId),
            spanId = ToHex(exemplar.SpanId),
            value = exemplar.ValueCase == Exemplar.ValueOneofCase.AsDouble ? exemplar.AsDouble : exemplar.AsInt,
            attributes = valueConverter.ToDictionary(exemplar.FilteredAttributes)
        }).ToArray();
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
}
