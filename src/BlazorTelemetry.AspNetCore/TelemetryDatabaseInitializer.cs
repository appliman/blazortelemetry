using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.AspNetCore;

internal sealed class TelemetryDatabaseInitializer(IServiceProvider serviceProvider, ILogger<TelemetryDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
