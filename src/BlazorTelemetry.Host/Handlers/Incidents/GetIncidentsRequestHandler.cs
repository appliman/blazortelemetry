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

internal sealed class GetIncidentsRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetIncidentsRequestHandler> logger) : IRequestHandler<GetIncidentsRequest, QueryResult<IReadOnlyList<Incident>>>
{
    public async Task<QueryResult<IReadOnlyList<Incident>>> Handle(GetIncidentsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetIncidents(request.IncludeResolved, cancellationToken);
            return new QueryResult<IReadOnlyList<Incident>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetIncidents failed.");
            return new QueryResult<IReadOnlyList<Incident>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<Incident>> GetIncidents(bool includeResolved, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.Incidents.AsNoTracking().AsQueryable();
        if (!includeResolved)
        {
            source = source.Where(incident => incident.State != IncidentState.Resolved);
        }

        return await source.OrderByDescending(incident => incident.StartedUtc).Take(200).ToListAsync(cancellationToken);
    }
}
