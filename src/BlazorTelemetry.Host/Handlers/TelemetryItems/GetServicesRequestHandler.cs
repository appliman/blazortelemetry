using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

namespace BlazorTelemetry.Host.Handlers.TelemetryItems;

internal sealed class GetServicesRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetServicesRequestHandler> logger) : IRequestHandler<GetServicesRequest, QueryResult<IReadOnlyList<string>>>
{
    public async Task<QueryResult<IReadOnlyList<string>>> Handle(GetServicesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetServices(request.FromUtc, request.ToUtc, cancellationToken);
            return new QueryResult<IReadOnlyList<string>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetServices failed.");
            return new QueryResult<IReadOnlyList<string>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<string>> GetServices(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await _context.TelemetryItems.AsNoTracking()
            .Where(_item => _item.TimestampUtc >= fromUtc && _item.TimestampUtc <= toUtc)
            .Select(_item => _item.ServiceName).Distinct().OrderBy(_name => _name).ToListAsync(cancellationToken);
    }
}
