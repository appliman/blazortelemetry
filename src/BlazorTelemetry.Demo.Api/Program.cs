using System.Diagnostics.Metrics;
using BlazorTelemetry.Client;
using OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);
var endpoint = new Uri(builder.Configuration["OtlpEndpoint"] ?? "http://localhost:5279");
var headers = builder.Configuration["OtlpHeaders"];
builder.Services.AddBlazorTelemetry(options =>
{
    options.ServiceName = "BlazorTelemetry.Demo.Api";
    options.ServiceVersion = "1.0.1";
    options.DeploymentEnvironment = builder.Environment.EnvironmentName;
    options.Endpoint = endpoint;
    options.Headers = headers;
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    options.AddMeter("BlazorTelemetry.Demo.Api");
});

var meter = new Meter("BlazorTelemetry.Demo.Api", "1.0.1");
var requests = meter.CreateCounter<long>("demo.api.orders", "{request}");
var duration = meter.CreateHistogram<double>("demo.api.duration", "ms");
var app = builder.Build();
app.MapGet("/orders/{id:int}", async (int id, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    var started = DateTime.UtcNow;
    using var scope = logger.BeginScope(new Dictionary<string, object?> { ["OrderId"] = id });
    logger.LogInformation("Reading order {OrderId}.", id);
    await Task.Delay(Random.Shared.Next(40, 180), cancellationToken);
    requests.Add(1, new KeyValuePair<string, object?>("operation", "read"));
    duration.Record((DateTime.UtcNow - started).TotalMilliseconds, new KeyValuePair<string, object?>("operation", "read"));
    return Results.Ok(new { id, status = "ready", total = 149.90 });
});
app.MapGet("/slow", async (CancellationToken cancellationToken) => { await Task.Delay(1500, cancellationToken); return Results.Ok(); });
app.MapGet("/fail", (ILogger<Program> logger) => { logger.LogError("Remote service demo error."); return Results.Problem("Controlled error."); });
app.Run();
