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

internal sealed class StoreTelemetryBatchRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<StoreTelemetryBatchRequestHandler> logger) : IRequestHandler<StoreTelemetryBatchRequest, CommandResult>
{
    private static readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<CommandResult> Handle(StoreTelemetryBatchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _writeGate.WaitAsync(cancellationToken);
            int changed;
            try
            {
                changed = await Store(request.Items, cancellationToken);
            }
            finally
            {
                _writeGate.Release();
            }
            return new CommandResult { ChangeCount = changed };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Store failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<int> Store(IReadOnlyCollection<TelemetryItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var inserted = 0;

        foreach (var chunk in items.Chunk(500))
        {
            var uniqueItems = chunk
                .Where(item => item.Fingerprint is null)
                .Concat(chunk
                    .Where(item => item.Fingerprint is not null)
                    .DistinctBy(item => item.Fingerprint, StringComparer.Ordinal))
                .ToArray();
            var fingerprints = uniqueItems
                .Where(item => item.Fingerprint is not null)
                .Select(item => item.Fingerprint!)
                .ToArray();
            HashSet<string> existingFingerprints = [];
            if (fingerprints.Length > 0)
            {
                existingFingerprints = (await context.TelemetryItems
                    .AsNoTracking()
                    .Where(item => item.Fingerprint != null && fingerprints.Contains(item.Fingerprint))
                    .Select(item => item.Fingerprint!)
                    .ToListAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);
            }

            var newItems = uniqueItems
                .Where(item => item.Fingerprint is null || !existingFingerprints.Contains(item.Fingerprint))
                .ToArray();
            inserted += newItems.Length;
            await context.TelemetryItems.AddRangeAsync(newItems, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            context.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }
}
