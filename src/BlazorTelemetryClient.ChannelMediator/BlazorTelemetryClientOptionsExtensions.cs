namespace BlazorTelemetry.Client.ChannelMediator;

public static class BlazorTelemetryClientOptionsExtensions
{
    public static BlazorTelemetryClientOptions AddChannelMediatorInstrumentation(
        this BlazorTelemetryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddSource(ChannelMediatorTelemetry.ACTIVITY_SOURCE_NAME);
        options.AddMeter(ChannelMediatorTelemetry.METER_NAME);
        return options;
    }
}
