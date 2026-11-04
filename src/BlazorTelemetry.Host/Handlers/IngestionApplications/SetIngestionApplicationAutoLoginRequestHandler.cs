using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.IngestionApplications;

internal sealed class SetIngestionApplicationAutoLoginRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<SetIngestionApplicationAutoLoginRequestHandler> logger)
    : IRequestHandler<SetIngestionApplicationAutoLoginRequest, CommandResult>
{
    public async Task<CommandResult> Handle(SetIngestionApplicationAutoLoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var _changed = await _context.IngestionApplications
                .Where(application => application.Id == request.Id && application.IsActive && application.RevokedUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(application => application.AllowAutoLogin, request.AllowAutoLogin), cancellationToken);
            return new CommandResult { ChangeCount = _changed };
        }
        catch (Exception _exception) when (_exception is not OperationCanceledException)
        {
            logger.LogError(_exception, "Failed to update the API key auto-login permission.");
            return new CommandResult
            {
                BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")]
            };
        }
    }
}
