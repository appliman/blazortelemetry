using System.Diagnostics.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var endpoint = new Uri(builder.Configuration["OtlpEndpoint"] ?? "http://localhost:5279");
var headers = builder.Configuration["OtlpHeaders"];
var resource = ResourceBuilder.CreateDefault().AddService("BlazorTelemetry.Demo.Api", serviceVersion: "1.0.1");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resourceBuilder => resourceBuilder.AddService("BlazorTelemetry.Demo.Api", serviceVersion: "1.0.1"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = request => request.RequestUri?.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal) != true)
        .AddOtlpExporter(options => Configure(options, endpoint, "v1/traces", headers)))
    .WithMetrics(metrics => metrics.AddMeter("BlazorTelemetry.Demo.Api").AddRuntimeInstrumentation().AddAspNetCoreInstrumentation().AddOtlpExporter(options => Configure(options, endpoint, "v1/metrics", headers)));
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.SetResourceBuilder(resource);
    logging.AddOtlpExporter(options => Configure(options, endpoint, "v1/logs", headers));
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

static void Configure(OtlpExporterOptions options, Uri endpoint, string signalPath, string? headers)
{
    options.Endpoint = new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/{signalPath}");
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    options.Headers = headers;
}
