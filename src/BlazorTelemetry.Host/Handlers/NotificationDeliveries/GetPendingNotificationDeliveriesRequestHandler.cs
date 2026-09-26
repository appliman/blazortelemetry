using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.NotificationDeliveries;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.NotificationDeliveries;

internal sealed class GetPendingNotificationDeliveriesRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetPendingNotificationDeliveriesRequestHandler> logger)
    : IRequestHandler<GetPendingNotificationDeliveriesRequest, QueryResult<IReadOnlyList<NotificationDelivery>>>
{
    public async Task<QueryResult<IReadOnlyList<NotificationDelivery>>> Handle(GetPendingNotificationDeliveriesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var deliveries = await context.NotificationDeliveries.AsNoTracking()
                .Where(delivery => delivery.Status == "pending" && delivery.NextAttemptUtc <= request.NowUtc)
                .OrderBy(delivery => delivery.NextAttemptUtc)
                .Take(20)
                .ToListAsync(cancellationToken);
            return new QueryResult<IReadOnlyList<NotificationDelivery>> { Data = deliveries };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to read pending notification deliveries.");
            return new QueryResult<IReadOnlyList<NotificationDelivery>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "Failed to read notification deliveries.")] };
        }
    }
}
