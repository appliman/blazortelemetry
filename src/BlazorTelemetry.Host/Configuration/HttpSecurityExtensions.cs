using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace BlazorTelemetry.Host.Configuration;

public static class HttpSecurityExtensions
{
    public static IServiceCollection AddTelemetryHttpSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Override the permissive ASPNETCORE_FORWARDEDHEADERS_ENABLED hosting shortcut as well.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
            foreach (var proxy in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
            foreach (var network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                var authentication = path.StartsWithSegments("/b2fa") || path.StartsWithSegments("/login")
                    || path.StartsWithSegments("/api/auth/auto-login") || path.StartsWithSegments("/auto-login");
                var ingestion = path.StartsWithSegments("/v1") || path.Value?.StartsWith("/opentelemetry.proto.collector.", StringComparison.Ordinal) == true;
                if (!authentication && !ingestion)
                {
                    return RateLimitPartition.GetNoLimiter("other");
                }

                var partition = $"{(authentication ? "authentication" : "ingestion")}:{context.Connection.RemoteIpAddress}";
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, configuration.GetValue<int?>(authentication ? "RateLimits:AuthenticationPerMinute" : "RateLimits:IngestionPerMinute") ?? (authentication ? 30 : 1200)),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });
        return services;
    }

    public static IApplicationBuilder UseTelemetryHttpSecurity(this IApplicationBuilder application)
    {
        return application.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                if (context.Request.Path.StartsWithSegments("/b2fa") || context.Request.Path.StartsWithSegments("/login")
                    || context.Request.Path.StartsWithSegments("/auto-login") || context.Request.Path.StartsWithSegments("/api/auth"))
                {
                    context.Response.Headers.CacheControl = "no-store";
                }
                return Task.CompletedTask;
            });
            await next(context);
        });
    }
}
