using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.IngestionApplications;

internal sealed class ValidateAutoLoginKeyRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<ValidateAutoLoginKeyRequestHandler> logger) : IRequestHandler<ValidateAutoLoginKeyRequest, QueryResult<IngestionApplication?>>
{
    public async Task<QueryResult<IngestionApplication?>> Handle(ValidateAutoLoginKeyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var _now = timeProvider.GetUtcNow();
            var _key = await _context.IngestionApplications.AsNoTracking().FirstOrDefaultAsync(
                value => value.KeyHash == request.KeyHash && value.IsActive && value.RevokedUtc == null
                    && (value.ExpiresUtc == null || value.ExpiresUtc > _now), cancellationToken);
            return new QueryResult<IngestionApplication?> { Data = _key };
        }
        catch (Exception _exception) when (_exception is not OperationCanceledException)
        {
            logger.LogError(_exception, "Auto-login API key validation failed.");
            return new QueryResult<IngestionApplication?>
            {
                BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")]
            };
        }
    }
}
