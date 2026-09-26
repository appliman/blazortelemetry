using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;
using ChannelMediator;

namespace BlazorTelemetry.Tests;

internal sealed class CoordinatorTestHandler(IngestionCoordinator coordinator, CoordinatorTestGate gate) : IRequestHandler<PersistTelemetryBatchRequest>
{
    public async Task Handle(PersistTelemetryBatchRequest request, CancellationToken cancellationToken)
    {
        if (gate.PauseBeforeStart)
        {
            await gate.AllowStart.Task;
        }
        if (!coordinator.TryStart(request.BatchId, out _))
        {
            return;
        }

        gate.Started.TrySetResult(true);
        var persisted = await gate.Commit.Task;
        coordinator.Complete(request.BatchId, persisted);
    }
}
