using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.Maintenance;
using BlazorTelemetry.Host.Contracts.Results;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.Maintenance;

internal sealed class InitializeTelemetryDatabaseRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<InitializeTelemetryDatabaseRequestHandler> logger)
    : IRequestHandler<InitializeTelemetryDatabaseRequest, CommandResult>
{
    public async Task<CommandResult> Handle(InitializeTelemetryDatabaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
            const string defaultRuleName = "More than 5 errors in one minute";
            if (!await context.AlertRules.AnyAsync(rule => rule.Name == defaultRuleName, cancellationToken))
            {
                context.AlertRules.Add(new AlertRule
                {
                    Name = defaultRuleName,
                    ServiceName = "*",
                    Type = AlertRuleType.ErrorLogs,
                    Threshold = 5,
                    WindowMinutes = 1,
                    ConfirmationMinutes = 0,
                    Severity = "warning",
                    IsEnabled = true
                });
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Created the default per-application error alert rule.");
            }

            logger.LogInformation("Telemetry database initialized with WAL journaling.");
            return new CommandResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Telemetry database initialization failed.");
            return new CommandResult { BrokenRules = [new BrokenRule("initialization_error", string.Empty, "Telemetry database initialization failed.")] };
        }
    }
}
