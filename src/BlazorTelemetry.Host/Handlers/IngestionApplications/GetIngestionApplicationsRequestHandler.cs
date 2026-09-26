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

internal sealed class GetIngestionApplicationsRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetIngestionApplicationsRequestHandler> logger) : IRequestHandler<GetIngestionApplicationsRequest, QueryResult<IReadOnlyList<IngestionApplication>>>
{
    public async Task<QueryResult<IReadOnlyList<IngestionApplication>>> Handle(GetIngestionApplicationsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetIngestionApplications(cancellationToken);
            return new QueryResult<IReadOnlyList<IngestionApplication>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetIngestionApplications failed.");
            return new QueryResult<IReadOnlyList<IngestionApplication>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<IngestionApplication>> GetIngestionApplications(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.IngestionApplications.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
    }
}
