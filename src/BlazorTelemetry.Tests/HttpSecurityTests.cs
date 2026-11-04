using System.Net;
using System.Security.Claims;
using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Host.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorTelemetry.Tests;

public sealed class HttpSecurityTests
{
    [Theory]
    [InlineData("198.51.100.5", false)]
    [InlineData("192.0.2.5", true)]
    public async Task ForwardedHeadersOnlyTrustConfiguredProxiesEvenAfterPermissiveHostingSetup(string peer, bool trusted)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "192.0.2.5"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });
        services.AddTelemetryHttpSecurity(configuration);
        using var provider = services.BuildServiceProvider();
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            provider.GetRequiredService<ILoggerFactory>(), provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        await middleware.Invoke(context);
        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(trusted ? "1.2.3.4" : peer, context.Connection.RemoteIpAddress?.ToString());
    }

    [Fact]
    public async Task DetailedHealthRequiresReaderWhileLivenessRemainsPublic()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton(new CollectorCounters());
        builder.Services.AddSingleton(new BlazorTelemetryOptions());
        builder.Services.AddSingleton<IngestionKeyAuthorizer>();
        builder.Services.AddSingleton<OtlpValueConverter>();
        builder.Services.AddSingleton<OtlpParser>();
        builder.Services.AddSingleton<IngestionCoordinator>();
        builder.Services.AddScoped<ChannelMediator.IMediator>(_ => throw new InvalidOperationException("Ingestion is not used by this health check test."));
        builder.Services.AddHealthChecks();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorizationBuilder().AddPolicy("BlazorTelemetryReader", policy => policy.RequireRole("Reader"));
        builder.Services.AddGrpc();
        await using var app = builder.Build();
        app.UseTelemetryHttpSecurity();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBlazorTelemetry();
        app.MapHealthChecks("/health/live").AllowAnonymous();
        app.MapGet("/test/sign-in", async (HttpContext context) =>
        {
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Reader")], CookieAuthenticationDefaults.AuthenticationScheme)));
        });
        await app.StartAsync();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };

        using var anonymous = await client.GetAsync("/blazor-telemetry/health");
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.DoesNotContain("ProcessWorkingSetBytes", await anonymous.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var signedIn = await client.GetAsync("/test/sign-in");
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using var reader = await client.GetAsync("/blazor-telemetry/health");
        Assert.Equal(HttpStatusCode.OK, reader.StatusCode);
        Assert.Contains("processWorkingSetBytes", await reader.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", Assert.Single(reader.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(reader.Headers.GetValues("Content-Security-Policy")));
        await app.StopAsync();
    }

    [Fact]
    public async Task AuthenticationAndIngestionHaveIndependentEffectiveRateLimits()
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimits:AuthenticationPerMinute"] = "2",
            ["RateLimits:IngestionPerMinute"] = "3"
        });
        builder.Services.AddTelemetryHttpSecurity(builder.Configuration);
        await using var app = builder.Build();
        app.UseTelemetryHttpSecurity();
        app.UseRateLimiter();
        app.MapPost("/api/auth/auto-login", () => Results.Ok());
        app.MapPost("/v1/logs", () => Results.Ok());
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };

        for (var index = 0; index < 2; index++)
        {
            using var response = await client.PostAsync("/api/auth/auto-login", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        using var rejectedLogin = await client.PostAsync("/api/auth/auto-login", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedLogin.StatusCode);
        Assert.NotNull(rejectedLogin.Headers.RetryAfter);
        for (var index = 0; index < 3; index++)
        {
            using var response = await client.PostAsync("/v1/logs", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var rejectedIngestion = await client.PostAsync("/v1/logs", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedIngestion.StatusCode);
        await app.StopAsync();
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }
}
