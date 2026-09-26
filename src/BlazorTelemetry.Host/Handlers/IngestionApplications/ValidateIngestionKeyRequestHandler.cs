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

internal sealed class ValidateIngestionKeyRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<ValidateIngestionKeyRequestHandler> logger) : IRequestHandler<ValidateIngestionKeyRequest, QueryResult<IngestionApplication?>>
{
    public async Task<QueryResult<IngestionApplication?>> Handle(ValidateIngestionKeyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await FindActiveIngestionApplication(request.KeyHash, cancellationToken);
            return new QueryResult<IngestionApplication?> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "FindActiveIngestionApplication failed.");
            return new QueryResult<IngestionApplication?> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IngestionApplication?> FindActiveIngestionApplication(string keyHash, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var application = await context.IngestionApplications.AsNoTracking().FirstOrDefaultAsync(item => item.IsActive && !item.IsMcpReadKey && item.KeyHash == keyHash && (item.ExpiresUtc == null || item.ExpiresUtc > now), cancellationToken);
        if (application is null)
        {
            return null;
        }

        var updateTime = DateTimeOffset.UtcNow;
        var updated = await context.IngestionApplications
            .Where(item => item.Id == application.Id && item.KeyHash == keyHash && item.IsActive && !item.IsMcpReadKey && (item.ExpiresUtc == null || item.ExpiresUtc > updateTime))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LastSeenUtc, updateTime)
                .SetProperty(item => item.UsageCount, item => item.UsageCount + 1), cancellationToken);
        return updated == 1 ? application : null;
    }
}
