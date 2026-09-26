using BlazorTelemetry.Host.Contracts.Models.DataProtection;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.DataProtection;

internal sealed class GetDataProtectionKeysRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetDataProtectionKeysRequestHandler> logger)
    : IRequestHandler<GetDataProtectionKeysRequest, QueryResult<IReadOnlyList<string>>>
{
    public async Task<QueryResult<IReadOnlyList<string>>> Handle(GetDataProtectionKeysRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var keys = await context.DataProtectionKeys.AsNoTracking()
                .Select(key => key.Xml!).ToListAsync(cancellationToken);
            return new QueryResult<IReadOnlyList<string>> { Data = keys };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to read data protection keys.");
            return new QueryResult<IReadOnlyList<string>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "Failed to read data protection keys.")] };
        }
    }
}
