using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

internal sealed class TelemetryWriterService(
    TelemetryIngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    CollectorCounters counters,
    ILogger<TelemetryWriterService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var batch in queue.Reader.ReadAllAsync(stoppingToken))
            {
                counters.ChangeQueueDepth(-1);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                    await repository.Store(batch.Items, stoppingToken);
                    counters.AddPersisted(batch.Items.Count);
                    batch.Completion.TrySetResult(true);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Failed to persist a batch of {ItemCount} telemetry items.", batch.Items.Count);
                    counters.AddRejected(batch.Items.Count);
                    batch.Completion.TrySetResult(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
