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
        logger.LogInformation("Telemetry database initialized with WAL journaling.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
