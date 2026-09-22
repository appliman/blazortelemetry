using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace BlazorTelemetry.Tests;

public sealed class OtlpParserTests
{
    private readonly OtlpParser _parser;

    public OtlpParserTests()
    {
        var options = new BlazorTelemetryOptions();
        _parser = new OtlpParser(new OtlpValueConverter(options));
    }

    [Fact]
    public void ParseLogsPreservesCorrelationAndMasksSecrets()
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs { Resource = CreateResource() };
        var scopeLogs = new ScopeLogs();
        scopeLogs.LogRecords.Add(new LogRecord
        {
            TimeUnixNano = 1_700_000_000_000_000_000,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "ERROR",
            Body = new AnyValue { StringValue = "Payment declined" },
            TraceId = ByteString.CopyFrom(Convert.FromHexString("00112233445566778899AABBCCDDEEFF")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("0011223344556677")),
            Attributes = { new KeyValue { Key = "authorization", Value = new AnyValue { StringValue = "Bearer secret" } } }
        });
        resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);

        var item = Assert.Single(_parser.ParseLogs(request, null));

        Assert.Equal("checkout", item.ServiceName);
        Assert.Equal("00112233445566778899aabbccddeeff", item.TraceId);
        Assert.Contains("[redacted]", item.AttributesJson);
        Assert.DoesNotContain("Bearer secret", item.AttributesJson);
    }

    [Fact]
    public void ParseTracesProducesStableFingerprint()
    {
        var request = new ExportTraceServiceRequest();
        var resourceSpans = new ResourceSpans { Resource = CreateResource() };
        var scopeSpans = new ScopeSpans();
        scopeSpans.Spans.Add(new Span
        {
            Name = "POST /orders",
            TraceId = ByteString.CopyFrom(Convert.FromHexString("00112233445566778899AABBCCDDEEFF")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("0011223344556677")),
            StartTimeUnixNano = 1_700_000_000_000_000_000,
            EndTimeUnixNano = 1_700_000_000_250_000_000,
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        });
        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);

        var first = Assert.Single(_parser.ParseTraces(request, null));
        var second = Assert.Single(_parser.ParseTraces(request, null));

        Assert.Equal(250, first.DurationMs);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void ParseTracesProjectsServerSpansAsExternalRequests()
    {
        var request = new ExportTraceServiceRequest();
        var resourceSpans = new ResourceSpans { Resource = CreateResource() };
        var scopeSpans = new ScopeSpans();
        scopeSpans.Spans.Add(new Span
        {
            Name = "GET /orders/{id}",
            Kind = Span.Types.SpanKind.Server,
            TraceId = ByteString.CopyFrom(Convert.FromHexString("00112233445566778899AABBCCDDEEFF")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("0011223344556677")),
            StartTimeUnixNano = 1_700_000_000_000_000_000,
            EndTimeUnixNano = 1_700_000_000_125_000_000,
            Attributes =
            {
                new KeyValue { Key = "url.full", Value = new AnyValue { StringValue = "https://shop.example.com/orders/42" } },
                new KeyValue { Key = "http.request.method", Value = new AnyValue { StringValue = "GET" } },
                new KeyValue { Key = "http.response.status_code", Value = new AnyValue { IntValue = 200 } },
                new KeyValue { Key = "client.address", Value = new AnyValue { StringValue = "203.0.113.10" } },
                new KeyValue { Key = "user_agent.original", Value = new AnyValue { StringValue = "External request agent" } }
            }
        });
        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);

        var items = _parser.ParseTraces(request, null);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.Kind == TelemetryKind.Trace);
        var externalRequest = Assert.Single(items, item => item.Kind == TelemetryKind.Request);
        Assert.Equal("checkout", externalRequest.ServiceName);
        Assert.Equal("https://shop.example.com/orders/42", externalRequest.Name);
        Assert.Equal("GET", externalRequest.Body);
        Assert.Equal(200, externalRequest.StatusCode);
        Assert.Equal(125, externalRequest.DurationMs);
        Assert.Contains("203.0.113.10", externalRequest.AttributesJson);
        Assert.Contains("External request agent", externalRequest.AttributesJson);
    }

    [Fact]
    public void ParseMetricsPreservesGaugeValue()
    {
        var request = new ExportMetricsServiceRequest();
        var resourceMetrics = new ResourceMetrics { Resource = CreateResource() };
        var scopeMetrics = new ScopeMetrics();
        var metric = new Metric { Name = "queue.depth", Unit = "{item}", Gauge = new Gauge() };
        metric.Gauge.DataPoints.Add(new NumberDataPoint { TimeUnixNano = 1_700_000_000_000_000_000, AsInt = 42 });
        scopeMetrics.Metrics.Add(metric);
        resourceMetrics.ScopeMetrics.Add(scopeMetrics);
        request.ResourceMetrics.Add(resourceMetrics);

        var item = Assert.Single(_parser.ParseMetrics(request, null));

        Assert.Equal(TelemetryKind.Metric, item.Kind);
        Assert.Equal("gauge", item.MetricType);
        Assert.Equal(42, item.NumericValue);
    }

    [Fact]
    public void ParseMetricsPreservesCounterStartAndDistinguishesInstances()
    {
        var _request = new ExportMetricsServiceRequest();
        foreach (var _instance in new[] { "a", "b" })
        {
            var _resource = CreateResource();
            _resource.Attributes.Add(new KeyValue { Key = "service.instance.id", Value = new AnyValue { StringValue = _instance } });
            _request.ResourceMetrics.Add(new ResourceMetrics { Resource = _resource, ScopeMetrics =
            {
                new ScopeMetrics { Metrics =
                {
                    new Metric { Name = "commands", Sum = new Sum { AggregationTemporality = AggregationTemporality.Cumulative, IsMonotonic = true,
                        DataPoints = { new NumberDataPoint { TimeUnixNano = 1_700_000_010_000_000_000, StartTimeUnixNano = 1_700_000_000_000_000_000, AsInt = 42 } } } }
                } }
            } });
        }
        var _items = _parser.ParseMetrics(_request, null);
        Assert.Equal(2, _items.Count);
        Assert.NotEqual(_items[0].Fingerprint, _items[1].Fingerprint);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), MetricCounter.StartTime(_items[0]));
    }
    private static Resource CreateResource()
    {
        return new Resource
        {
            Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } }
        };
    }

}
