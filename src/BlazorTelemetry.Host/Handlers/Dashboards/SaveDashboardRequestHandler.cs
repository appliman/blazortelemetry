using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.Dashboards;
using FluentValidation;

namespace BlazorTelemetry.Host.Handlers.Dashboards;

internal sealed class SaveDashboardRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    IValidator<DashboardDefinition> validator,
    ILogger<SaveDashboardRequestHandler> logger) : IRequestHandler<SaveDashboardRequest, PersistResult>
{
    public async Task<PersistResult> Handle(SaveDashboardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var validation = await validator.ValidateAsync(request.Dashboard, cancellationToken);
            if (!validation.IsValid)
            {
                return new PersistResult { BrokenRules = validation.Errors.Select(error => new BrokenRule("validation", error.PropertyName, error.ErrorMessage)).ToArray() };
            }

            var isNewEntity = request.Dashboard.Id == 0;
            await SaveDashboard(request.Dashboard, cancellationToken);
            return new PersistResult { Id = request.Dashboard.Id, IsNewEntity = isNewEntity, ChangeCount = 1 };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SaveDashboard failed.");
            return new PersistResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task SaveDashboard(DashboardDefinition dashboard, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        dashboard.UpdatedUtc = DateTimeOffset.UtcNow;
        context.Update(dashboard);
        await context.SaveChangesAsync(cancellationToken);
    }
}
