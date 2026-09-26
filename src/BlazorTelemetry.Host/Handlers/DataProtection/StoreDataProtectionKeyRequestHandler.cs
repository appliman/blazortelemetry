using BlazorTelemetry.Host.Contracts.Models.DataProtection;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.DataProtection;

internal sealed class StoreDataProtectionKeyRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<StoreDataProtectionKeyRequestHandler> logger)
    : IRequestHandler<StoreDataProtectionKeyRequest, CommandResult>
{
    public async Task<CommandResult> Handle(StoreDataProtectionKeyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.DataProtectionKeys.AddAsync(new DataProtectionKey
            {
                FriendlyName = request.FriendlyName,
                Xml = request.Xml
            }, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return new CommandResult { ChangeCount = 1 };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to store a data protection key.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "Failed to store a data protection key.")] };
        }
    }
}
