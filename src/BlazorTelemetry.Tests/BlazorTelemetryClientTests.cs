using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using BlazorTelemetry.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;

namespace BlazorTelemetry.Tests;

public sealed class BlazorTelemetryClientTests
{
    [Fact]
    public void AddBlazorTelemetryRegistersPreconfiguredClientOptions()
    {
        var services = new ServiceCollection();

        services.AddBlazorTelemetry(options =>
        {
            options.ServiceName = "orders-api";
            options.ServiceVersion = "2.3.4";
            options.DeploymentEnvironment = "test";
            options.Endpoint = new Uri("https://telemetry.example.com");
            options.Headers = "X-BlazorTelemetry-Key=test-key";
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.AddSource("Orders.Activities");
            options.AddMeter("Orders.Metrics");
            options.AddResourceAttribute("service.namespace", "commerce");
            options.ExcludePath("internal/status");
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BlazorTelemetryClientOptions>();

        Assert.Equal("orders-api", options.ServiceName);
        Assert.Equal("2.3.4", options.ServiceVersion);
        Assert.Equal("test", options.DeploymentEnvironment);
        Assert.Equal(new Uri("https://telemetry.example.com"), options.Endpoint);
        Assert.Equal("X-BlazorTelemetry-Key=test-key", options.Headers);
        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
        Assert.Contains("Orders.Activities", options.Sources);
        Assert.Contains("Orders.Metrics", options.Meters);
        Assert.Contains(options.ResourceAttributes, attribute => attribute.Key == "service.namespace" && Equals(attribute.Value, "commerce"));
        Assert.Contains("/internal/status", options.ExcludedPathPrefixes);
    }

    [Fact]
    public void AddBlazorTelemetryEnablesAllSignalsAndGrpcByDefault()
    {
        var services = new ServiceCollection();

        services.AddBlazorTelemetry(options => options.ServiceName = "default-service");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BlazorTelemetryClientOptions>();

        Assert.True(options.LogsEnabled);
        Assert.True(options.TracesEnabled);
        Assert.True(options.MetricsEnabled);
        Assert.True(options.AspNetCoreInstrumentationEnabled);
        Assert.True(options.EntityFrameworkCoreInstrumentationEnabled);
        Assert.True(options.EntityFrameworkMetricsEnabled);
        Assert.True(options.HttpClientInstrumentationEnabled);
        Assert.True(options.RuntimeInstrumentationEnabled);
        Assert.False(options.CaptureDatabaseStatements);
        Assert.True(options.CaptureClientAddress);
        Assert.True(options.CaptureNetworkAddresses);
        Assert.True(options.CaptureHttpHeaders);
        Assert.True(options.CaptureHttpBodySizes);
        Assert.Contains("Microsoft.AspNetCore.Components", options.Sources);
        Assert.Contains("Microsoft.AspNetCore.Components.Server.Circuits", options.Sources);
        Assert.Contains("Microsoft.AspNetCore.Components", options.Meters);
        Assert.Contains("Microsoft.AspNetCore.Components.Lifecycle", options.Meters);
        Assert.Contains("Microsoft.AspNetCore.Components.Server.Circuits", options.Meters);
        Assert.Contains("Accept-Language", options.RequestHeaders);
        Assert.DoesNotContain("Authorization", options.RequestHeaders);
        Assert.DoesNotContain("Cookie", options.RequestHeaders);
        Assert.DoesNotContain("Set-Cookie", options.ResponseHeaders);
        Assert.Equal(OtlpExportProtocol.Grpc, options.Protocol);
    }

    [Fact]
    public async Task EntityFrameworkTelemetryEnricherRecordsUsefulLowCardinalityMetrics()
    {
        var measurements = new List<(string Name, double Value, IReadOnlyDictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == EntityFrameworkTelemetryEnricher.METER_NAME)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add((instrument.Name, measurement, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            measurements.Add((instrument.Name, measurement, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        meterListener.Start();

        using var enricher = new EntityFrameworkTelemetryEnricher();
        await enricher.StartAsync(CancellationToken.None);
        using var source = new ActivitySource("OpenTelemetry.Instrumentation.EntityFrameworkCore");
        using (var activity = source.StartActivity("SELECT", ActivityKind.Client))
        {
            Assert.NotNull(activity);
            activity.SetTag("db.system.name", "sqlite");
            activity.SetTag("db.operation.name", "SELECT");
            activity.SetTag("error.type", "Microsoft.Data.Sqlite.SqliteException");
            activity.SetStatus(ActivityStatusCode.Error);
        }

        await enricher.StopAsync(CancellationToken.None);

        Assert.Contains(measurements, measurement => measurement.Name == EntityFrameworkTelemetryEnricher.COMMANDS_METRIC_NAME && measurement.Value == 1);
        Assert.Contains(measurements, measurement => measurement.Name == EntityFrameworkTelemetryEnricher.FAILURES_METRIC_NAME && measurement.Value == 1);
        Assert.Contains(measurements, measurement => measurement.Name == EntityFrameworkTelemetryEnricher.ACTIVE_COMMANDS_METRIC_NAME && measurement.Value == 1);
        Assert.Contains(measurements, measurement => measurement.Name == EntityFrameworkTelemetryEnricher.ACTIVE_COMMANDS_METRIC_NAME && measurement.Value == -1);
        Assert.Contains(measurements, measurement => measurement.Name == EntityFrameworkTelemetryEnricher.DURATION_METRIC_NAME && measurement.Value >= 0);
        Assert.Contains(measurements, measurement => measurement.Tags.TryGetValue("db.system.name", out var value) && Equals(value, "sqlite"));
        Assert.Contains(measurements, measurement => measurement.Tags.TryGetValue("db.operation.name", out var value) && Equals(value, "SELECT"));
        Assert.Contains(measurements, measurement => measurement.Tags.TryGetValue("db.operation.type", out var value) && Equals(value, "read"));
    }

    [Theory]
    [InlineData("SELECT Id FROM Products", "read")]
    [InlineData("INSERT INTO Products (Name) VALUES ('test')", "insert")]
    [InlineData("UPDATE Products SET Name = 'test'", "update")]
    [InlineData("DELETE FROM Products", "delete")]
    public void EntityFrameworkTelemetryEnricherClassifiesDatabaseOperations(string commandText, string expectedOperation)
    {
        using var activity = new Activity("database-command").Start();
        using var command = new SqliteCommand(commandText);

        EntityFrameworkTelemetryEnricher.Enrich(activity, command);

        Assert.Equal(expectedOperation, activity.GetTagItem("db.operation.type"));
        Assert.Equal("text", activity.GetTagItem("db.command.type"));
    }

    [Fact]
    public void HttpTelemetryEnricherCapturesClientNetworkAndSafeRequestMetadata()
    {
        var options = new BlazorTelemetryClientOptions();
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "request-42";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        context.Connection.RemotePort = 55000;
        context.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.5");
        context.Connection.LocalPort = 8080;
        context.Request.ContentLength = 123;
        context.Request.Headers.AcceptLanguage = "fr-FR";
        context.Request.Headers.Authorization = "Bearer secret";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        context.Response.ContentLength = 456;
        context.Response.Headers.ContentType = "application/json";
        using var activity = new Activity("test-request").Start();

        HttpTelemetryEnricher.EnrichRequest(activity, context.Request, options);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Connection.RemotePort = 54321;
        HttpTelemetryEnricher.EnrichResponse(activity, context.Response, options);

        var tags = activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value);
        Assert.Equal("203.0.113.10", tags["client.address"]);
        Assert.Equal(54321, tags["client.port"]);
        Assert.Equal("10.0.0.2", tags["network.peer.address"]);
        Assert.Equal("10.0.0.5", tags["network.local.address"]);
        Assert.Equal("request-42", tags["http.request.id"]);
        Assert.Equal(123L, tags["http.request.body.size"]);
        Assert.Equal(456L, tags["http.response.body.size"]);
        Assert.Equal("fr-FR", tags["http.request.header.accept-language"]);
        Assert.Equal("application/json", tags["http.response.header.content-type"]);
        Assert.Equal(true, tags["http.request.forwarded"]);
        Assert.DoesNotContain("http.request.header.authorization", tags.Keys);
        Assert.DoesNotContain("http.request.header.x-forwarded-for", tags.Keys);
    }

    [Fact]
    public void AddBlazorTelemetryRejectsMissingEndpoint()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddBlazorTelemetry(options =>
        {
            options.ServiceName = "orders-api";
            options.Endpoint = null;
        }));

        Assert.Equal("BlazorTelemetry requires an absolute OTLP endpoint.", exception.Message);
    }
}
