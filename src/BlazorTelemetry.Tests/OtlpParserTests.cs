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

    private static Resource CreateResource()
    {
        return new Resource
        {
            Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } }
        };
    }
}
