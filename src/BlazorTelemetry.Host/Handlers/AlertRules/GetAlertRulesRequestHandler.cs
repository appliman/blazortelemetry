using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.AlertRules;

namespace BlazorTelemetry.Host.Handlers.AlertRules;

internal sealed class GetAlertRulesRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetAlertRulesRequestHandler> logger) : IRequestHandler<GetAlertRulesRequest, QueryResult<IReadOnlyList<AlertRule>>>
{
    public async Task<QueryResult<IReadOnlyList<AlertRule>>> Handle(GetAlertRulesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetAlertRules(cancellationToken);
            return new QueryResult<IReadOnlyList<AlertRule>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetAlertRules failed.");
            return new QueryResult<IReadOnlyList<AlertRule>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<AlertRule>> GetAlertRules(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AlertRules.AsNoTracking().OrderBy(rule => rule.Name).ToListAsync(cancellationToken);
    }
}
