using System.Collections.Concurrent;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;
using ChannelMediator.InMemory;

namespace BlazorTelemetry.AspNetCore;

internal sealed class IngestionCoordinator : IDisposable
{
    private readonly ConcurrentDictionary<Guid, PendingBatch> _batches = new();
    private readonly SemaphoreSlim _slots;
    private readonly CollectorCounters _counters;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeSpan _timeout;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private int _disposed;

    public IngestionCoordinator(BlazorTelemetryOptions options, CollectorCounters counters, IHostApplicationLifetime lifetime)
    {
        _slots = new SemaphoreSlim(options.QueueCapacity, options.QueueCapacity);
        _counters = counters;
        _lifetime = lifetime;
        _timeout = TimeSpan.FromSeconds(options.IngestionAcknowledgementTimeoutSeconds);
        _stoppingRegistration = lifetime.ApplicationStopping.Register(Stop);
    }

    public async Task<(bool Accepted, bool Persisted)> Enqueue(IReadOnlyList<TelemetryItem> items, IMediator mediator, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || _lifetime.ApplicationStopping.IsCancellationRequested)
        {
            _counters.AddRejected(items.Count);
            return (false, false);
        }

        bool slotAvailable;
        try
        {
            slotAvailable = _slots.Wait(0);
        }
        catch (ObjectDisposedException)
        {
            _counters.AddRejected(items.Count);
            return (false, false);
        }

        if (!slotAvailable || Volatile.Read(ref _disposed) != 0)
        {
            if (slotAvailable)
            {
                ReleaseSlot();
            }
            _counters.AddRejected(items.Count);
            return (false, false);
        }

        var batchId = Guid.NewGuid();
        var batch = new PendingBatch(items);
        _batches[batchId] = batch;
        _counters.AddReceived(items.Count);
        _counters.ChangeQueueDepth(1);
        try
        {
            await mediator.EnqueueRequest(new PersistTelemetryBatchRequest(batchId), _lifetime.ApplicationStopping);
            return (true, await batch.Completion.Task.WaitAsync(_timeout, cancellationToken));
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            Abandon(batchId, batch);
            return (true, false);
        }
        catch
        {
            Abandon(batchId, batch);
            throw;
        }
    }

    public bool TryStart(Guid batchId, out IReadOnlyList<TelemetryItem> items)
    {
        items = [];
        if (!_batches.TryGetValue(batchId, out var batch))
        {
            return false;
        }

        lock (batch)
        {
            if (batch.State != BatchState.Pending)
            {
                return false;
            }

            batch.State = BatchState.Processing;
            items = batch.Items;
            return true;
        }
    }

    public void Complete(Guid batchId, bool persisted)
    {
        if (!_batches.TryRemove(batchId, out var batch))
        {
            return;
        }

        if (persisted)
        {
            _counters.AddPersisted(batch.Items.Count);
        }
        else
        {
            _counters.AddRejected(batch.Items.Count);
        }

        batch.Completion.TrySetResult(persisted);
        _counters.ChangeQueueDepth(-1);
        ReleaseSlot();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stoppingRegistration.Dispose();
        Stop();
        _slots.Dispose();
    }

    private void Abandon(Guid batchId, PendingBatch batch)
    {
        lock (batch)
        {
            if (batch.State == BatchState.Pending)
            {
                batch.State = BatchState.Abandoned;
                Complete(batchId, false);
            }
            else
            {
                batch.Completion.TrySetResult(false);
            }
        }
    }

    private void Stop()
    {
        foreach (var entry in _batches)
        {
            Abandon(entry.Key, entry.Value);
        }
    }

    private void ReleaseSlot()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

}
