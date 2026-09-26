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

internal sealed class RotateIngestionApplicationRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    IValidator<IngestionApplication> validator,
    ILogger<RotateIngestionApplicationRequestHandler> logger) : IRequestHandler<RotateIngestionApplicationRequest, CommandResult>
{
    public async Task<CommandResult> Handle(RotateIngestionApplicationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await RotateIngestionApplication(request.Id, request.Replacement, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "RotateIngestionApplication failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<CommandResult> RotateIngestionApplication(int id, IngestionApplication replacement, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var oldKey = await context.IngestionApplications.FirstOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
        if (oldKey is null)
        {
            return new CommandResult();
        }

        oldKey.IsActive = false;
        oldKey.RevokedUtc = now;
        replacement.Name = oldKey.Name;
        replacement.IsMcpReadKey = oldKey.IsMcpReadKey;
        replacement.ExpiresUtc = oldKey.ExpiresUtc > now ? oldKey.ExpiresUtc : null;
        var validation = await validator.ValidateAsync(replacement, cancellationToken);
        if (!validation.IsValid)
        {
            return new CommandResult { BrokenRules = validation.Errors.Select(error => new BrokenRule("validation", error.PropertyName, error.ErrorMessage)).ToArray() };
        }
        context.IngestionApplications.Update(oldKey);
        await context.IngestionApplications.AddAsync(replacement, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CommandResult { ChangeCount = 1 };
    }
}
