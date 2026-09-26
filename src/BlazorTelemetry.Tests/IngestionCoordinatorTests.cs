using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using ChannelMediator;
using ChannelMediator.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BlazorTelemetry.Tests;

public sealed class IngestionCoordinatorTests
{
    [Fact]
    public async Task CapacityIsHeldUntilPersistenceCompletes()
    {
        using var services = CreateServices();
        var coordinator = services.GetRequiredService<IngestionCoordinator>();
        var mediator = services.GetRequiredService<IMediator>();
        var handler = services.GetRequiredService<CoordinatorTestGate>();
        var counters = services.GetRequiredService<CollectorCounters>();
        var first = coordinator.Enqueue([new TelemetryItem()], services.GetRequiredService<IMediator>(), CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rejected = await coordinator.Enqueue([new TelemetryItem()], services.GetRequiredService<IMediator>(), CancellationToken.None);
        Assert.False(rejected.Accepted);
        Assert.False(rejected.Persisted);
        Assert.Equal(1, counters.QueueDepth);
        handler.Commit.SetResult(true);
        Assert.Equal((true, true), await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, counters.QueueDepth);
        Assert.Equal(1, counters.Persisted);
        Assert.Equal(1, counters.Rejected);
    }

    [Fact]
    public async Task FailedPersistenceRejectsTheBatch()
    {
        using var services = CreateServices();
        var coordinator = services.GetRequiredService<IngestionCoordinator>();
        var handler = services.GetRequiredService<CoordinatorTestGate>();
        var pending = coordinator.Enqueue([new TelemetryItem()], services.GetRequiredService<IMediator>(), CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Commit.SetResult(false);

        Assert.Equal((true, false), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, services.GetRequiredService<CollectorCounters>().QueueDepth);
    }

    [Fact]
    public async Task ClientCancellationKeepsTheSlotUntilProcessingCompletes()
    {
        using var services = CreateServices();
        var coordinator = services.GetRequiredService<IngestionCoordinator>();
        var handler = services.GetRequiredService<CoordinatorTestGate>();
        using var cancellation = new CancellationTokenSource();
        var pending = coordinator.Enqueue([new TelemetryItem()], services.GetRequiredService<IMediator>(), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Assert.Equal((true, false), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, services.GetRequiredService<CollectorCounters>().QueueDepth);
        handler.Commit.SetResult(true);
        await WaitUntilEmpty(coordinator, services.GetRequiredService<CollectorCounters>());
        Assert.Equal(1, services.GetRequiredService<CollectorCounters>().Persisted);
    }

    [Fact]
    public async Task HostStoppingCompletesWaitingClientAndReleasesAfterProcessing()
    {
        using var services = CreateServices();
        var coordinator = services.GetRequiredService<IngestionCoordinator>();
        var handler = services.GetRequiredService<CoordinatorTestGate>();
        var counters = services.GetRequiredService<CollectorCounters>();
        var pending = coordinator.Enqueue([new TelemetryItem()], services.GetRequiredService<IMediator>(), CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        services.GetRequiredService<TestHostApplicationLifetime>().StopApplication();

        Assert.Equal((true, false), await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, counters.QueueDepth);
        handler.Commit.SetResult(false);
        await WaitUntilEmpty(coordinator, counters);
        Assert.Equal(1, counters.Rejected);
    }

    [Fact]
    public async Task ExpiredBatchBeforeProcessingReleasesCapacity()
    {
        using var services = CreateServices(1);
        var coordinator = services.GetRequiredService<IngestionCoordinator>();
        var gate = services.GetRequiredService<CoordinatorTestGate>();
        var counters = services.GetRequiredService<CollectorCounters>();
        gate.PauseBeforeStart = true;
        var pending = coordinator.Enqueue([new TelemetryItem()], services.GetRequiredService<IMediator>(), CancellationToken.None);

        Assert.Equal((true, false), await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, counters.QueueDepth);
        Assert.Equal(1, counters.Rejected);
        gate.AllowStart.SetResult(true);
        await Task.Delay(50);
        Assert.False(gate.Started.Task.IsCompleted);
    }

    private static ServiceProvider CreateServices(int timeoutSeconds = 5)
    {
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton(new BlazorTelemetryOptions { QueueCapacity = 1, IngestionAcknowledgementTimeoutSeconds = timeoutSeconds });
        registrations.AddSingleton<CollectorCounters>();
        registrations.AddSingleton<TestHostApplicationLifetime>();
        registrations.AddSingleton<IHostApplicationLifetime>(provider => provider.GetRequiredService<TestHostApplicationLifetime>());
        registrations.AddSingleton<IngestionCoordinator>();
        registrations.AddSingleton<CoordinatorTestGate>();
        registrations.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(CoordinatorTestHandler).Assembly);
        var provider = registrations.BuildServiceProvider();
        foreach (var service in provider.GetServices<IHostedService>())
        {
            service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        return provider;
    }

    private static async Task WaitUntilEmpty(IngestionCoordinator coordinator, CollectorCounters counters)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (counters.QueueDepth != 0)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
