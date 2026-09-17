using System.Diagnostics;
using System.Text.Json;
using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITelemetryRepository repository, IHostEnvironment environment)
    {
        var _path = context.Request.Path;
        if (_path.StartsWithSegments("/_blazor") ||
            _path.StartsWithSegments("/_framework") || _path.StartsWithSegments("/_content") ||
            _path.Equals(new PathString("/v1/logs")) || _path.Equals(new PathString("/v1/traces")) ||
            _path.Equals(new PathString("/v1/metrics")) || _path.Equals(new PathString("/blazor-telemetry/health")) ||
            _path.Value?.StartsWith("/opentelemetry.proto.collector.", StringComparison.Ordinal) == true)
        {
            await next(context);
            return;
        }

        var _requestId = Guid.NewGuid().ToString("N");
        var _started = Stopwatch.GetTimestamp();
        var _item = new TelemetryItem
        {
            Kind = TelemetryKind.Request,
            TimestampUtc = DateTimeOffset.UtcNow,
            ObservedUtc = DateTimeOffset.UtcNow,
            ServiceName = environment.ApplicationName,
            Name = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{_path}",
            Body = context.Request.Method,
            TraceId = _requestId,
            AttributesJson = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["client.address"] = context.Connection.RemoteIpAddress?.ToString(),
                ["user_agent.original"] = context.Request.Headers.UserAgent.ToString()
            })
        };
        try
        {
            await repository.Store([_item], context.RequestAborted);
        }
        catch (Exception _exception)
        {
            logger.LogWarning(_exception, "Unable to record HTTP request telemetry.");
        }

        var _failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            _failed = true;
            throw;
        }
        finally
        {
            var _duration = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            using var _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await repository.CompleteRequest(_requestId, _duration,
                    _failed ? 500 : context.RequestAborted.IsCancellationRequested ? 499 : context.Response.StatusCode, _timeout.Token);
            }
            catch (Exception _exception)
            {
                logger.LogWarning(_exception, "Unable to complete HTTP request telemetry.");
            }
        }
    }
}
