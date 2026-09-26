using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.Incidents;

namespace BlazorTelemetry.Host.Handlers.Incidents;

internal sealed class AcknowledgeIncidentRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<AcknowledgeIncidentRequestHandler> logger) : IRequestHandler<AcknowledgeIncidentRequest, CommandResult>
{
    public async Task<CommandResult> Handle(AcknowledgeIncidentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var changed = await AcknowledgeIncident(request.Id, cancellationToken);
            return new CommandResult { ChangeCount = changed };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "AcknowledgeIncident failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<int> AcknowledgeIncident(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return await context.Incidents.Where(incident => incident.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(incident => incident.AcknowledgedUtc, now), cancellationToken);
    }
}
