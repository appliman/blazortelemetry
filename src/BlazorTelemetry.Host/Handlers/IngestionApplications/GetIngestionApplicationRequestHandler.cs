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

internal sealed class GetIngestionApplicationRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetIngestionApplicationRequestHandler> logger) : IRequestHandler<GetIngestionApplicationRequest, QueryResult<IngestionApplication?>>
{
    public async Task<QueryResult<IngestionApplication?>> Handle(GetIngestionApplicationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetIngestionApplication(request.Id, cancellationToken);
            return new QueryResult<IngestionApplication?> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetIngestionApplication failed.");
            return new QueryResult<IngestionApplication?> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IngestionApplication?> GetIngestionApplication(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.IngestionApplications.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }
}
