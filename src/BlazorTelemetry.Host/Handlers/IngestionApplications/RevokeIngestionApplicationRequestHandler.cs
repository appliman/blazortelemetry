using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;

namespace BlazorTelemetry.Host.Handlers.IngestionApplications;

internal sealed class RevokeIngestionApplicationRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<RevokeIngestionApplicationRequestHandler> logger) : IRequestHandler<RevokeIngestionApplicationRequest, CommandResult>
{
    public async Task<CommandResult> Handle(RevokeIngestionApplicationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var changed = await RevokeIngestionApplication(request.Id, cancellationToken);
            return new CommandResult { ChangeCount = changed ? 1 : 0 };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "RevokeIngestionApplication failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<bool> RevokeIngestionApplication(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return await context.IngestionApplications.Where(item => item.Id == id && item.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false)
                .SetProperty(item => item.RevokedUtc, now), cancellationToken) == 1;
    }
}
