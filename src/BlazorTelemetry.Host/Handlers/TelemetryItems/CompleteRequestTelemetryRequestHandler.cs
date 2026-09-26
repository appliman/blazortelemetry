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

internal sealed class CompleteRequestTelemetryRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<CompleteRequestTelemetryRequestHandler> logger) : IRequestHandler<CompleteRequestTelemetryRequest, CommandResult>
{
    public async Task<CommandResult> Handle(CompleteRequestTelemetryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var changed = await CompleteRequest(request.RequestId, request.DurationMs, request.StatusCode, cancellationToken);
            return new CommandResult { ChangeCount = changed };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "CompleteRequest failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<int> CompleteRequest(string requestId, double durationMs, int statusCode, CancellationToken cancellationToken)
    {
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await _context.TelemetryItems.Where(_item => _item.Kind == TelemetryKind.Request && _item.TraceId == requestId)
            .ExecuteUpdateAsync(_setters => _setters.SetProperty(_item => _item.DurationMs, durationMs)
                .SetProperty(_item => _item.StatusCode, statusCode), cancellationToken);
    }
}
