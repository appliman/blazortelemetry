using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.Dashboards;

namespace BlazorTelemetry.Host.Handlers.Dashboards;

internal sealed class GetDashboardsRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetDashboardsRequestHandler> logger) : IRequestHandler<GetDashboardsRequest, QueryResult<IReadOnlyList<DashboardDefinition>>>
{
    public async Task<QueryResult<IReadOnlyList<DashboardDefinition>>> Handle(GetDashboardsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetDashboards(cancellationToken);
            return new QueryResult<IReadOnlyList<DashboardDefinition>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetDashboards failed.");
            return new QueryResult<IReadOnlyList<DashboardDefinition>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<DashboardDefinition>> GetDashboards(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Dashboards.AsNoTracking().OrderBy(dashboard => dashboard.Name).ToListAsync(cancellationToken);
    }
}
