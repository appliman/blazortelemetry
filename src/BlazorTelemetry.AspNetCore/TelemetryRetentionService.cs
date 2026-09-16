using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

internal sealed class TelemetryRetentionService(
    IServiceScopeFactory scopeFactory,
    BlazorTelemetryOptions options,
    CollectorCounters counters,
    ILogger<TelemetryRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                var now = DateTimeOffset.UtcNow;
                var purged = await repository.Purge(now.AddDays(-options.RawRetentionDays), now.AddDays(-options.MetricRetentionDays), options.MaximumDatabaseBytes, stoppingToken);
                counters.AddPurged(purged);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telemetry cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
