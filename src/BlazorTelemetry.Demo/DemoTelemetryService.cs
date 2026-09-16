using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BlazorTelemetry.Demo;

public sealed class DemoTelemetryService(ILogger<DemoTelemetryService> logger, IHttpClientFactory httpClientFactory)
{
    public const string ACTIVITY_SOURCE_NAME = "BlazorTelemetry.Demo";
    public const string METER_NAME = "BlazorTelemetry.Demo";
    private static readonly ActivitySource ACTIVITY_SOURCE = new(ACTIVITY_SOURCE_NAME);
    private static readonly Meter METER = new(METER_NAME, "1.0.1");
    private static readonly Counter<long> ORDERS = METER.CreateCounter<long>("demo.orders", "{order}", "Simulated orders");
    private static readonly Counter<long> FAILURES = METER.CreateCounter<long>("demo.failures", "{failure}", "Simulated errors");
    private static readonly Histogram<double> DURATION = METER.CreateHistogram<double>("demo.order.duration", "ms", "Order processing duration");

    public async Task<string> Run(string scenario, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ACTIVITY_SOURCE.StartActivity($"demo.{scenario}", ActivityKind.Internal);
        activity?.SetTag("demo.scenario", scenario);
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["Scenario"] = scenario, ["OrderId"] = Random.Shared.Next(1000, 9999) });
        logger.LogInformation("Starting scenario {Scenario}.", scenario);
        try
        {
            switch (scenario)
            {
                case "success":
                    await Task.Delay(Random.Shared.Next(40, 140), cancellationToken);
                    ORDERS.Add(1, new KeyValuePair<string, object?>("status", "completed"));
                    logger.LogInformation("The order was processed successfully.");
                    break;
                case "slow":
                    await Task.Delay(1100, cancellationToken);
                    ORDERS.Add(1, new KeyValuePair<string, object?>("status", "slow"));
                    logger.LogWarning("The order exceeded the latency target.");
                    break;
                case "remote":
                    var response = await httpClientFactory.CreateClient("demo-api").GetAsync("/orders/42", cancellationToken);
                    response.EnsureSuccessStatusCode();
                    logger.LogInformation("The remote service responded with status {StatusCode}.", (int)response.StatusCode);
                    break;
                case "failure":
                    FAILURES.Add(1, new KeyValuePair<string, object?>("type", "payment"));
                    activity?.SetStatus(ActivityStatusCode.Error, "Payment declined");
                    throw new InvalidOperationException("The payment provider declined the transaction.");
            }
            return "Scenario completed. Telemetry will appear within a few seconds.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Controlled failure in scenario {Scenario}.", scenario);
            return $"Controlled error: {exception.Message}";
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            DURATION.Record(elapsed, new KeyValuePair<string, object?>("scenario", scenario));
            activity?.SetTag("demo.duration_ms", elapsed);
        }
    }
}
