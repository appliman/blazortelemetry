using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.ModelExtensions;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace BlazorTelemetry.Tests;

public sealed partial class TelemetryCqrsTests
{
    [Fact]
    public async Task OtlpMetricVariantsPersistTypedScalarsAndChildren()
    {
        const ulong TIMESTAMP = 1790848800000000000;
        var _exemplar = new Exemplar { TimeUnixNano = TIMESTAMP, AsDouble = 2, TraceId = ByteString.CopyFrom(new byte[16]), SpanId = ByteString.CopyFrom(new byte[8]) };
        _exemplar.FilteredAttributes.Add(new KeyValue { Key = "label", Value = new AnyValue { StringValue = "sample" } });
        var _histogram = new HistogramDataPoint { TimeUnixNano = TIMESTAMP, StartTimeUnixNano = TIMESTAMP - 1000000000, Count = 3, Sum = 5, Min = 1, Max = 3 };
        _histogram.ExplicitBounds.Add(2);
        _histogram.BucketCounts.Add(new ulong[] { 1, 2 });
        _histogram.Exemplars.Add(_exemplar);
        var _exponential = new ExponentialHistogramDataPoint
        {
            TimeUnixNano = TIMESTAMP, Count = 4, Sum = 7, Scale = 2, ZeroCount = 1,
            Positive = new() { Offset = 3, BucketCounts = { 2 } }, Negative = new() { Offset = -2, BucketCounts = { 1 } }
        };
        var _summary = new SummaryDataPoint { TimeUnixNano = TIMESTAMP, Count = 2, Sum = 4 };
        _summary.QuantileValues.Add(new SummaryDataPoint.Types.ValueAtQuantile { Quantile = 0.95, Value = 3 });
        var _scope = new ScopeMetrics { Scope = new InstrumentationScope { Name = "scope", Version = "1" } };
        _scope.Metrics.Add(new Metric { Name = "gauge", Gauge = new Gauge { DataPoints = { new NumberDataPoint { TimeUnixNano = TIMESTAMP, AsInt = 42 } } } });
        _scope.Metrics.Add(new Metric { Name = "sum", Sum = new Sum { IsMonotonic = true, AggregationTemporality = AggregationTemporality.Delta, DataPoints = { new NumberDataPoint { TimeUnixNano = TIMESTAMP, StartTimeUnixNano = TIMESTAMP - 1000000000, AsDouble = 5 } } } });
        _scope.Metrics.Add(new Metric { Name = "histogram", Histogram = new Histogram { AggregationTemporality = AggregationTemporality.Cumulative, DataPoints = { _histogram } } });
        _scope.Metrics.Add(new Metric { Name = "exponential", ExponentialHistogram = new ExponentialHistogram { AggregationTemporality = AggregationTemporality.Delta, DataPoints = { _exponential } } });
        _scope.Metrics.Add(new Metric { Name = "summary", Summary = new Summary { DataPoints = { _summary } } });
        var _request = new ExportMetricsServiceRequest { ResourceMetrics = { new ResourceMetrics { ScopeMetrics = { _scope } } } };
        var _parser = new OtlpParser(new OtlpValueConverter(new BlazorTelemetryOptions()));
        await _mediator.Store(_parser.ParseMetrics(_request, "api"), CancellationToken.None);
        var _items = (await _mediator.Query(new TelemetryQuery(TelemetryKind.Metric), CancellationToken.None)).Items.ToDictionary(_item => _item.Name);
        Assert.Equal(42, _items["gauge"].NumericValue);
        Assert.True(_items["sum"].IsMonotonic);
        Assert.Equal("Delta", _items["sum"].AggregationTemporality);
        Assert.NotNull(_items["sum"].StartTimeUtc);
        Assert.Equal(3, _items["histogram"].Count);
        Assert.Equal(5, _items["histogram"].Sum);
        Assert.Equal(1, _items["histogram"].Minimum);
        Assert.Equal(3, _items["histogram"].Maximum);
        Assert.Equal(2, _items["histogram"].Buckets.Count);
        Assert.Equal(2, _items["histogram"].Buckets[0].UpperBound);
        Assert.Null(_items["histogram"].Buckets[1].UpperBound);
        Assert.Contains("sample", Assert.Single(_items["histogram"].Exemplars).AttributesJson);
        Assert.Equal(2, _items["exponential"].Scale);
        Assert.Equal(1, _items["exponential"].ZeroCount);
        Assert.Equal(3, _items["exponential"].PositiveOffset);
        Assert.Equal(-2, _items["exponential"].NegativeOffset);
        Assert.Equal(new[] { -1, 1 }, _items["exponential"].Buckets.Select(_bucket => _bucket.Group).Order().ToArray());
        Assert.Equal(0.95, Assert.Single(_items["summary"].Quantiles).Quantile);
        Assert.All(_items.Values, _item => Assert.Equal("scope", _item.ScopeName));
        Assert.All(_items.Values, _item => Assert.Equal("1", _item.ScopeVersion));
    }

    [Fact]
    public async Task ServerSpanKeepsTraceAndRequestWithTypedHttpContext()
    {
        var _span = new Span
        {
            Name = "GET /orders", Kind = Span.Types.SpanKind.Server, StartTimeUnixNano = 1790848800000000000,
            EndTimeUnixNano = 1790848800010000000, TraceId = ByteString.CopyFrom(new byte[16]), SpanId = ByteString.CopyFrom(new byte[8])
        };
        _span.Attributes.Add(new KeyValue { Key = "client.address", Value = new AnyValue { StringValue = "203.0.113.1" } });
        _span.Attributes.Add(new KeyValue { Key = "http.request.method", Value = new AnyValue { StringValue = "GET" } });
        _span.Attributes.Add(new KeyValue { Key = "http.route", Value = new AnyValue { StringValue = "/orders" } });
        _span.Events.Add(new Span.Types.Event { Name = "event", TimeUnixNano = _span.StartTimeUnixNano });
        _span.Links.Add(new Span.Types.Link { TraceId = _span.TraceId, SpanId = _span.SpanId });
        var _request = new ExportTraceServiceRequest { ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { _span } } } } } };
        var _parser = new OtlpParser(new OtlpValueConverter(new BlazorTelemetryOptions()));
        await _mediator.Store(_parser.ParseTraces(_request, "api"), CancellationToken.None);
        var _page = await _mediator.Query(new TelemetryQuery(TraceId: new string('0', 32)), CancellationToken.None);
        Assert.Equal(2, _page.Total);
        var _trace = Assert.Single(_page.Items, _item => _item.Kind == TelemetryKind.Trace);
        Assert.Equal("event", Assert.Single(_trace.Events).Name);
        Assert.Single(_trace.Links);
        var _http = Assert.Single(_page.Items, _item => _item.Kind == TelemetryKind.Request);
        Assert.Equal("Server", _trace.SpanKind);
        Assert.Equal("OTLP server span", _http.Source);
        Assert.Equal("GET", _http.HttpMethod);
        Assert.Equal("/orders", _http.Route);
        Assert.Equal("203.0.113.1", _http.ClientAddress);
        Assert.DoesNotContain("http.request.method", _http.AttributesJson);
        Assert.Contains("http.request.method", _trace.AttributesJson);
    }

    [Fact]
    public void OtlpMetricsRejectOverflowWithoutSilentNumericTruncation()
    {
        var _parser = new OtlpParser(new OtlpValueConverter(new BlazorTelemetryOptions()));
        var _scope = new ScopeMetrics { Metrics = { new Metric { Name = "large", Histogram = new Histogram { DataPoints = { new HistogramDataPoint { Count = ulong.MaxValue } } } } } };
        var _request = new ExportMetricsServiceRequest { ResourceMetrics = { new ResourceMetrics { ScopeMetrics = { _scope } } } };
        Assert.Throws<OverflowException>(() => _parser.ParseMetrics(_request, "api"));
        _scope.Metrics.Clear();
        _scope.Metrics.Add(new Metric { Name = "integer", Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 9007199254740993 } } } });
        Assert.Throws<OverflowException>(() => _parser.ParseMetrics(_request, "api"));
    }
}
