using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTelemetry.Tests;

public sealed class IngestionKeyAuthorizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task IngestionRequiresANonemptyKeyByDefault(string? supplied)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = new BlazorTelemetryOptions { IngestionKeys = new() { ["unconfigured"] = "" } };
        var context = new DefaultHttpContext();
        if (supplied is not null)
        {
            context.Request.Headers["X-BlazorTelemetry-Key"] = supplied;
        }
        var authorizer = new IngestionKeyAuthorizer(options, services.GetRequiredService<IServiceScopeFactory>());
        Assert.False((await authorizer.Authorize(context.Request, CancellationToken.None)).IsAuthorized);
    }

    [Fact]
    public async Task ConfiguredKeyAuthorizesItsApplication()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = new BlazorTelemetryOptions { IngestionKeys = new() { ["api"] = "test-ingestion-key" } };
        var context = new DefaultHttpContext();
        context.Request.Headers["X-BlazorTelemetry-Key"] = "test-ingestion-key";
        var authorizer = new IngestionKeyAuthorizer(options, services.GetRequiredService<IServiceScopeFactory>());
        var result = await authorizer.Authorize(context.Request, CancellationToken.None);
        Assert.True(result.IsAuthorized);
        Assert.Equal("api", result.ApplicationName);
    }
}
