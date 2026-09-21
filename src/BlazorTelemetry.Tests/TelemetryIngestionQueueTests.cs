using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;

namespace BlazorTelemetry.Tests;

public sealed class TelemetryIngestionQueueTests
{
    [Fact]
    public async Task FullQueueRejectsBatchWithoutLeavingAnIncompleteWaiter()
    {
        var counters = new CollectorCounters();
        var queue = new TelemetryIngestionQueue(
            new BlazorTelemetryOptions { QueueCapacity = 1 },
            counters);
        var item = new TelemetryItem
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ObservedUtc = DateTimeOffset.UtcNow,
            ServiceName = "test",
            Name = "queue-test"
        };

        var firstWrite = queue.Enqueue([item], CancellationToken.None);
        var secondWrite = queue.Enqueue([item], CancellationToken.None);

        Assert.False(await secondWrite.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, counters.QueueDepth);
        Assert.Equal(1, counters.Received);
        Assert.Equal(1, counters.Rejected);

        var acceptedBatch = await queue.Reader.ReadAsync();
        acceptedBatch.Completion.SetResult(true);

        Assert.True(await firstWrite.WaitAsync(TimeSpan.FromSeconds(1)));
    }
}
