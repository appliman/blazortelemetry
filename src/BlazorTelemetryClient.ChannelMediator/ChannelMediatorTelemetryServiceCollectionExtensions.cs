using global::ChannelMediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;

namespace BlazorTelemetry.Client.ChannelMediator;

public static class ChannelMediatorTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddChannelMediatorTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(TimingBehavior<,>)));
        return services;
    }

    public static OpenTelemetryBuilder AddBlazorTelemetryChannelMediator(
        this IServiceCollection services,
        Action<BlazorTelemetryClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddChannelMediatorTelemetry();
        return services.AddBlazorTelemetry(options =>
        {
            options.AddChannelMediatorInstrumentation();
            configure?.Invoke(options);
        });
    }
}
