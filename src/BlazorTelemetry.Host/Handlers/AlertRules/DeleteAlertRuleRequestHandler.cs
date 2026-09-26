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

internal sealed class DeleteAlertRuleRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<DeleteAlertRuleRequestHandler> logger) : IRequestHandler<DeleteAlertRuleRequest, CommandResult>
{
    public async Task<CommandResult> Handle(DeleteAlertRuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var changed = await DeleteAlertRule(request.Id, cancellationToken);
            return new CommandResult { ChangeCount = changed };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "DeleteAlertRule failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<int> DeleteAlertRule(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AlertRules.Where(rule => rule.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
