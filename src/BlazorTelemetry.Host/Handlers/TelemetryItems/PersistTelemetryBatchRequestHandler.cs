using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

namespace BlazorTelemetry.Host.Handlers.TelemetryItems;

internal sealed class PersistTelemetryBatchRequestHandler(
    IngestionCoordinator coordinator,
    IMediator mediator,
    ILogger<PersistTelemetryBatchRequestHandler> logger) : IRequestHandler<PersistTelemetryBatchRequest>
{
    public async Task Handle(PersistTelemetryBatchRequest request, CancellationToken cancellationToken)
    {
        if (!coordinator.TryStart(request.BatchId, out var items))
        {
            return;
        }

        var persisted = false;
        try
        {
            var result = await mediator.Send(new StoreTelemetryBatchRequest(items), cancellationToken);
            persisted = !result.HasError;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to persist a batch of {ItemCount} telemetry items.", items.Count);
        }
        finally
        {
            coordinator.Complete(request.BatchId, persisted);
        }
    }
}
