using BlazorTelemetry.Host.Contracts.Models.NotificationDeliveries;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.NotificationDeliveries;

internal sealed class RecordNotificationDeliveryResultRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<RecordNotificationDeliveryResultRequestHandler> logger)
    : IRequestHandler<RecordNotificationDeliveryResultRequest, CommandResult>
{
    public async Task<CommandResult> Handle(RecordNotificationDeliveryResultRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var delivery = await context.NotificationDeliveries.FirstOrDefaultAsync(item => item.Id == request.Id && item.Status == "pending", cancellationToken);
            if (delivery is null)
            {
                return new CommandResult { BrokenRules = [new BrokenRule("not_found", nameof(request.Id), "Notification delivery is no longer pending.")] };
            }

            if (request.Succeeded)
            {
                delivery.Status = "sent";
                delivery.SentUtc = request.NowUtc;
                delivery.LastError = null;
            }
            else
            {
                delivery.Attempts++;
                delivery.LastError = request.Error is { Length: > 500 } ? request.Error[..500] : request.Error;
                delivery.NextAttemptUtc = request.NowUtc.AddSeconds(Math.Min(900, Math.Pow(2, Math.Min(delivery.Attempts, 9)) * 5));
                if (delivery.Attempts >= 10)
                {
                    delivery.Status = "failed";
                }
            }

            context.NotificationDeliveries.Update(delivery);
            await context.SaveChangesAsync(cancellationToken);
            return new CommandResult { ChangeCount = 1 };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to update notification delivery {DeliveryId}.", request.Id);
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "Failed to update notification delivery.")] };
        }
    }
}
