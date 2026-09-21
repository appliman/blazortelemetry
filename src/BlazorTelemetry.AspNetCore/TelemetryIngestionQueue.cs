using System.Threading.Channels;
using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

internal sealed class TelemetryIngestionQueue
{
    private readonly Channel<IngestionBatch> _channel;
    private readonly CollectorCounters _counters;

    public TelemetryIngestionQueue(BlazorTelemetryOptions options, CollectorCounters counters)
    {
        _counters = counters;
        _channel = Channel.CreateBounded<IngestionBatch>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<IngestionBatch> Reader => _channel.Reader;

    public async Task<bool> Enqueue(IReadOnlyList<TelemetryItem> items, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = new IngestionBatch(items, completion);
        if (!_channel.Writer.TryWrite(batch))
        {
            _counters.AddRejected(items.Count);
            return false;
        }

        _counters.AddReceived(items.Count);
        _counters.ChangeQueueDepth(1);
        return await completion.Task.WaitAsync(cancellationToken);
    }
}
