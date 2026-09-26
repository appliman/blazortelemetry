using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;
using FluentValidation;

namespace BlazorTelemetry.Host.Handlers.IngestionApplications;

internal sealed class SaveIngestionApplicationRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    IValidator<IngestionApplication> validator,
    ILogger<SaveIngestionApplicationRequestHandler> logger) : IRequestHandler<SaveIngestionApplicationRequest, PersistResult>
{
    public async Task<PersistResult> Handle(SaveIngestionApplicationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var validation = await validator.ValidateAsync(request.Application, cancellationToken);
            if (!validation.IsValid)
            {
                return new PersistResult { BrokenRules = validation.Errors.Select(error => new BrokenRule("validation", error.PropertyName, error.ErrorMessage)).ToArray() };
            }

            var isNewEntity = request.Application.Id == 0;
            await SaveIngestionApplication(request.Application, cancellationToken);
            return new PersistResult { Id = request.Application.Id, IsNewEntity = isNewEntity, ChangeCount = 1 };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SaveIngestionApplication failed.");
            return new PersistResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task SaveIngestionApplication(IngestionApplication application, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (application.Id == 0)
        {
            await context.IngestionApplications.AddAsync(application, cancellationToken);
        }
        else
        {
            context.IngestionApplications.Update(application);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
