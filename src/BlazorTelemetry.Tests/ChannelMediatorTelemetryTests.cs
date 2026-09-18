using System.Diagnostics;
using BlazorTelemetry.Client;
using BlazorTelemetry.Client.ChannelMediator;
using ChannelMediator;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTelemetry.Tests;

public sealed class ChannelMediatorTelemetryTests
{
    [Fact]
    public void AddBlazorTelemetryChannelMediatorRegistersPipelineAndSignals()
    {
        var services = new ServiceCollection();

        services.AddBlazorTelemetryChannelMediator(options => options.ServiceName = "channelmediator-tests");

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>)
                && descriptor.ImplementationType == typeof(TimingBehavior<,>));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BlazorTelemetryClientOptions>();
        Assert.Contains(ChannelMediatorTelemetry.ACTIVITY_SOURCE_NAME, options.Sources);
        Assert.Contains(ChannelMediatorTelemetry.METER_NAME, options.Meters);
    }

    [Fact]
    public async Task TimingBehaviorCreatesSuccessfulHandlerActivity()
    {
        Activity? completedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ChannelMediatorTelemetry.ACTIVITY_SOURCE_NAME,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => completedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        var behavior = new TimingBehavior<ChannelMediatorTelemetryTestRequest, string>();
        var result = await behavior.HandleAsync(
            new ChannelMediatorTelemetryTestRequest(),
            () => ValueTask.FromResult("completed"),
            CancellationToken.None);

        Assert.Equal("completed", result);
        Assert.NotNull(completedActivity);
        Assert.Equal(ActivityStatusCode.Ok, completedActivity.Status);
        Assert.Equal(
            typeof(ChannelMediatorTelemetryTestRequest).FullName,
            completedActivity.GetTagItem("channelmediator.request.type"));
    }

    [Fact]
    public async Task TimingBehaviorRecordsHandlerFailure()
    {
        Activity? completedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ChannelMediatorTelemetry.ACTIVITY_SOURCE_NAME,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => completedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        var behavior = new TimingBehavior<ChannelMediatorTelemetryTestRequest, string>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.HandleAsync(
                new ChannelMediatorTelemetryTestRequest(),
                () => throw new InvalidOperationException("Handler failed."),
                CancellationToken.None));

        Assert.Equal("Handler failed.", exception.Message);
        Assert.NotNull(completedActivity);
        Assert.Equal(ActivityStatusCode.Error, completedActivity.Status);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            completedActivity.GetTagItem("error.type"));
    }
}
