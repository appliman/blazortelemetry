using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.Incidents;

namespace BlazorTelemetry.AspNetCore;

internal sealed class AlertEvaluationService(
    IServiceScopeFactory scopeFactory,
    BlazorTelemetryOptions options,
    ILogger<AlertEvaluationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.EvaluationIntervalSeconds)));
        do
        {
            await Evaluate(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task Evaluate(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new EvaluateAlertsRequest(), cancellationToken);
        if (result.HasError)
        {
            logger.LogError("Alert evaluation failed: {Error}", result.BrokenRules[0].Message);
        }
    }
}
