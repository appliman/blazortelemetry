using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.Maintenance;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class MaintenanceExtensions
{
    public static async Task<int> Purge(this IMediator mediator, DateTimeOffset rawBeforeUtc, DateTimeOffset metricsBeforeUtc, long maximumBytes, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new PurgeRequest(rawBeforeUtc, metricsBeforeUtc, maximumBytes), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.ChangeCount;
    }

}
