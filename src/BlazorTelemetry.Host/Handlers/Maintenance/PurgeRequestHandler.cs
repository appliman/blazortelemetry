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
        var purged = await context.TelemetryItems.Where(item => item.Kind != TelemetryKind.Metric && item.TimestampUtc < rawBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        purged += await context.TelemetryItems.Where(item => item.Kind == TelemetryKind.Metric && item.TimestampUtc < metricsBeforeUtc).ExecuteDeleteAsync(cancellationToken);

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        while (await GetUsedDatabaseBytes(connection, cancellationToken) > maximumBytes)
        {
            var removed = await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM TelemetryItems WHERE Id IN (SELECT Id FROM TelemetryItems ORDER BY TimestampUtc LIMIT 10000)",
                cancellationToken);
            if (removed == 0)
            {
                break;
            }

            purged += removed;
        }

        await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);", cancellationToken);

        return purged;
    }
}
