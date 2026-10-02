using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.Maintenance;

namespace BlazorTelemetry.Host.Handlers.Maintenance;

internal sealed class PurgeRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<PurgeRequestHandler> logger) : IRequestHandler<PurgeRequest, CommandResult>
{
    public async Task<CommandResult> Handle(PurgeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var count = await Purge(request.RawBeforeUtc, request.MetricsBeforeUtc, request.MaximumBytes, cancellationToken);
            return new CommandResult { ChangeCount = count };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Purge failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<int> Purge(DateTimeOffset rawBeforeUtc, DateTimeOffset metricsBeforeUtc, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var purged = await context.TelemetryLogs.Where(_item => _item.TimestampUtc < rawBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        purged += await context.TelemetryTraces.Where(_item => _item.TimestampUtc < rawBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        purged += await context.TelemetryRequest.Where(_item => _item.TimestampUtc < rawBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        purged += await context.TelemetryMetrics.Where(_item => _item.TimestampUtc < metricsBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        await context.DeleteOrphanResources(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        while (await GetUsedDatabaseBytes(connection, cancellationToken) > maximumBytes)
        {
            var oldest = await context.QueryTelemetry().OrderBy(_item => _item.TimestampUtc)
                .ThenBy(_item => _item.Kind).ThenBy(_item => _item.Id)
                .Select(_item => new { _item.Kind, _item.Id }).Take(10000).ToListAsync(cancellationToken);
            if (oldest.Count == 0)
            {
                break;
            }
            foreach (var group in oldest.GroupBy(_item => _item.Kind))
            {
                foreach (var chunk in group.Select(_item => _item.Id).Chunk(500))
                {
                    purged += group.Key switch
                    {
                        TelemetryKind.Log => await context.TelemetryLogs.Where(_item => chunk.Contains(_item.Id)).ExecuteDeleteAsync(cancellationToken),
                        TelemetryKind.Trace => await context.TelemetryTraces.Where(_item => chunk.Contains(_item.Id)).ExecuteDeleteAsync(cancellationToken),
                        TelemetryKind.Metric => await context.TelemetryMetrics.Where(_item => chunk.Contains(_item.Id)).ExecuteDeleteAsync(cancellationToken),
                        TelemetryKind.Request => await context.TelemetryRequest.Where(_item => chunk.Contains(_item.Id)).ExecuteDeleteAsync(cancellationToken),
                        _ => 0
                    };
                }
            }
            await context.DeleteOrphanResources(cancellationToken);
        }

        await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);", cancellationToken);

        return purged;
    }
}
