using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BlazorTelemetry.Client.ChannelMediator;

public static class ChannelMediatorTelemetry
{
    public const string ACTIVITY_SOURCE_NAME = "ChannelMediator.Handlers";
    public const string METER_NAME = "BlazorTelemetry.ChannelMediator";
    public const string REQUESTS_METRIC_NAME = "blazortelemetry.channel_mediator.requests";
    public const string FAILURES_METRIC_NAME = "blazortelemetry.channel_mediator.failures";
    public const string ACTIVE_REQUESTS_METRIC_NAME = "blazortelemetry.channel_mediator.active_requests";
    public const string DURATION_METRIC_NAME = "blazortelemetry.channel_mediator.request.duration";

    private static readonly string? _version = typeof(ChannelMediatorTelemetry).Assembly.GetName().Version?.ToString();

    internal static readonly ActivitySource ActivitySource = new(ACTIVITY_SOURCE_NAME, _version);
    internal static readonly Meter Meter = new(METER_NAME, _version);
    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        REQUESTS_METRIC_NAME,
        "{request}",
        "Number of completed ChannelMediator requests.");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        FAILURES_METRIC_NAME,
        "{request}",
        "Number of failed ChannelMediator requests.");
    internal static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
        ACTIVE_REQUESTS_METRIC_NAME,
        "{request}",
        "Number of ChannelMediator requests currently executing.");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        DURATION_METRIC_NAME,
        "ms",
        "ChannelMediator request execution duration.");
}
