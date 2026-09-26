using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.AlertRules;
using FluentValidation;

namespace BlazorTelemetry.Host.Handlers.AlertRules;

internal sealed class SaveAlertRuleRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    IValidator<AlertRule> validator,
    ILogger<SaveAlertRuleRequestHandler> logger) : IRequestHandler<SaveAlertRuleRequest, PersistResult>
{
    public async Task<PersistResult> Handle(SaveAlertRuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var validation = await validator.ValidateAsync(request.Rule, cancellationToken);
            if (!validation.IsValid)
            {
                return new PersistResult { BrokenRules = validation.Errors.Select(error => new BrokenRule("validation", error.PropertyName, error.ErrorMessage)).ToArray() };
            }

            var isNewEntity = request.Rule.Id == 0;
            await SaveAlertRule(request.Rule, cancellationToken);
            return new PersistResult { Id = request.Rule.Id, IsNewEntity = isNewEntity, ChangeCount = 1 };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SaveAlertRule failed.");
            return new PersistResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task SaveAlertRule(AlertRule rule, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Update(rule);
        await context.SaveChangesAsync(cancellationToken);
    }
}
