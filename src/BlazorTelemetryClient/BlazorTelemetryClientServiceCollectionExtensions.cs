using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BlazorTelemetry.Client;

public static class BlazorTelemetryClientServiceCollectionExtensions
{
    public static OpenTelemetryBuilder AddBlazorTelemetry(
        this IServiceCollection services,
        Action<BlazorTelemetryClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = CreateOptions();
        configure?.Invoke(options);
        Normalize(options);

        services.TryAddSingleton(options);
        ConfigureEntityFrameworkMetrics(services, options);
        if (options.MetricsEnabled && options.ProcessInstrumentationEnabled)
        {
            services.AddHostedService<ProcessTelemetry>();
        }
        ConfigureLogging(services, options);

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, options));

        if (options.TracesEnabled)
        {
            builder.WithTracing(tracing => ConfigureTracing(tracing, options));
        }

        if (options.MetricsEnabled)
        {
            builder.WithMetrics(metrics => ConfigureMetrics(metrics, options));
        }

        return builder;
    }

    private static BlazorTelemetryClientOptions CreateOptions()
    {
        var entryAssembly = Assembly.GetEntryAssembly()?.GetName();
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var protocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");

        return new BlazorTelemetryClientOptions
        {
            ServiceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? entryAssembly?.Name,
            ServiceVersion = entryAssembly?.Version?.ToString(),
            DeploymentEnvironment = environment,
            Endpoint = Uri.TryCreate(endpoint, UriKind.Absolute, out var configuredEndpoint)
                ? configuredEndpoint
                : new Uri("http://localhost:4317"),
            LogsEndpoint = ReadEndpoint("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"),
            TracesEndpoint = ReadEndpoint("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"),
            MetricsEndpoint = ReadEndpoint("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"),
            Headers = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"),
            LogsHeaders = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_HEADERS"),
            TracesHeaders = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_HEADERS"),
            MetricsHeaders = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_METRICS_HEADERS"),
            Protocol = string.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpExportProtocol.HttpProtobuf
                : OtlpExportProtocol.Grpc,
            LogsProtocol = ReadProtocol("OTEL_EXPORTER_OTLP_LOGS_PROTOCOL"),
            TracesProtocol = ReadProtocol("OTEL_EXPORTER_OTLP_TRACES_PROTOCOL"),
            MetricsProtocol = ReadProtocol("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL")
        };
    }

    private static void Normalize(BlazorTelemetryClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            throw new InvalidOperationException("BlazorTelemetry requires a service name.");
        }

        if (options.Endpoint is null || !options.Endpoint.IsAbsoluteUri)
        {
            throw new InvalidOperationException("BlazorTelemetry requires an absolute OTLP endpoint.");
        }

        foreach (var endpoint in new[] { options.LogsEndpoint, options.TracesEndpoint, options.MetricsEndpoint })
        {
            if (endpoint is not null && !endpoint.IsAbsoluteUri)
            {
                throw new InvalidOperationException("BlazorTelemetry signal-specific OTLP endpoints must be absolute.");
            }
        }

        if (options.MaximumAttributeLength <= 0)
        {
            throw new InvalidOperationException("BlazorTelemetry requires a positive maximum attribute length.");
        }

        options.ServiceName = options.ServiceName.Trim();
    }

    private static void ConfigureResource(ResourceBuilder resource, BlazorTelemetryClientOptions options)
    {
        resource.AddService(options.ServiceName!, serviceVersion: options.ServiceVersion);

        if (!string.IsNullOrWhiteSpace(options.DeploymentEnvironment))
        {
            resource.AddAttributes([new("deployment.environment.name", options.DeploymentEnvironment)]);
        }

        if (options.ResourceAttributes.Count > 0)
        {
            resource.AddAttributes(options.ResourceAttributes);
        }

        options.ConfigureResource?.Invoke(resource);
    }

    private static void ConfigureLogging(IServiceCollection services, BlazorTelemetryClientOptions options)
    {
        if (!options.LogsEnabled)
        {
            return;
        }

        services.AddLogging(logging => logging.AddOpenTelemetry(loggingOptions =>
        {
            loggingOptions.IncludeFormattedMessage = options.IncludeFormattedLogMessage;
            loggingOptions.IncludeScopes = options.IncludeLogScopes;
            loggingOptions.ParseStateValues = options.ParseLogStateValues;
            loggingOptions.SetResourceBuilder(CreateResource(options));
            loggingOptions.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, OtlpSignal.Logs));
            options.ConfigureLogging?.Invoke(loggingOptions);
        }));
    }

    private static void ConfigureEntityFrameworkMetrics(IServiceCollection services, BlazorTelemetryClientOptions options)
    {
        if (!options.MetricsEnabled
            || !options.EntityFrameworkCoreInstrumentationEnabled
            || !options.EntityFrameworkMetricsEnabled)
        {
            return;
        }

        services.TryAddSingleton<EntityFrameworkTelemetryEnricher>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<EntityFrameworkTelemetryEnricher>());
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, BlazorTelemetryClientOptions options)
    {
        if (options.AspNetCoreInstrumentationEnabled)
        {
            tracing.AddAspNetCoreInstrumentation(instrumentation =>
            {
                instrumentation.RecordException = options.RecordExceptions;
                instrumentation.Filter = context => IsIncluded(context.Request.Path, options)
                    && (options.AspNetCoreRequestFilter?.Invoke(context) ?? true);
                instrumentation.EnrichWithHttpRequest = (activity, request) => HttpTelemetryEnricher.EnrichRequest(activity, request, options);
                instrumentation.EnrichWithHttpResponse = (activity, response) => HttpTelemetryEnricher.EnrichResponse(activity, response, options);
                instrumentation.EnrichWithException = (activity, exception) => HttpTelemetryEnricher.EnrichException(activity, exception, options);
            });
        }

        if (options.HttpClientInstrumentationEnabled)
        {
            tracing.AddHttpClientInstrumentation(instrumentation =>
            {
                instrumentation.RecordException = options.RecordExceptions;
                instrumentation.FilterHttpRequestMessage = request => IsIncluded(request.RequestUri?.AbsolutePath, options)
                    && (options.HttpClientRequestFilter?.Invoke(request) ?? true);
                instrumentation.EnrichWithHttpRequestMessage = (activity, request) => HttpTelemetryEnricher.EnrichRequest(activity, request, options);
                instrumentation.EnrichWithHttpResponseMessage = (activity, response) => HttpTelemetryEnricher.EnrichResponse(activity, response, options);
                instrumentation.EnrichWithException = (activity, exception) => HttpTelemetryEnricher.EnrichHttpClientException(activity, exception, options);
            });
        }

        if (options.EntityFrameworkCoreInstrumentationEnabled)
        {
            tracing.AddEntityFrameworkCoreInstrumentation(instrumentation =>
            {
                options.ConfigureEntityFrameworkCore?.Invoke(instrumentation);
                var enrich = instrumentation.EnrichWithIDbCommand;
                instrumentation.EnrichWithIDbCommand = (activity, command) =>
                {
                    EntityFrameworkTelemetryEnricher.Enrich(activity, command);

                    if (options.CaptureDatabaseStatements)
                    {
                        activity.SetTag("db.query.text", Truncate(command.CommandText, options.MaximumAttributeLength));
                    }

                    enrich?.Invoke(activity, command);
                };
            });
        }

        foreach (var source in options.Sources.Distinct(StringComparer.Ordinal))
        {
            tracing.AddSource(source);
        }

        tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, OtlpSignal.Traces));
        options.ConfigureTracing?.Invoke(tracing);
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, BlazorTelemetryClientOptions options)
    {
        if (options.ProcessInstrumentationEnabled)
        {
            metrics.AddMeter(ProcessTelemetry.METER_NAME);
        }
        if (options.AspNetCoreInstrumentationEnabled)
        {
            metrics.AddAspNetCoreInstrumentation();
        }

        if (options.HttpClientInstrumentationEnabled)
        {
            metrics.AddHttpClientInstrumentation();
        }

        if (options.RuntimeInstrumentationEnabled)
        {
            metrics.AddRuntimeInstrumentation();
        }

        if (options.EntityFrameworkCoreInstrumentationEnabled && options.EntityFrameworkMetricsEnabled)
        {
            metrics.AddMeter(EntityFrameworkTelemetryEnricher.METER_NAME);
        }

        foreach (var meter in options.Meters.Distinct(StringComparer.Ordinal))
        {
            metrics.AddMeter(meter);
        }

        metrics.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, OtlpSignal.Metrics));
        options.ConfigureMetrics?.Invoke(metrics);
    }

    private static ResourceBuilder CreateResource(BlazorTelemetryClientOptions options)
    {
        var resource = ResourceBuilder.CreateDefault();
        ConfigureResource(resource, options);
        return resource;
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        BlazorTelemetryClientOptions options,
        OtlpSignal signal)
    {
        var protocol = GetProtocol(options, signal);
        exporter.Protocol = protocol;
        exporter.Endpoint = BuildEndpoint(GetEndpoint(options, signal), protocol, signal);
        exporter.Headers = GetHeaders(options, signal);
        options.ConfigureExporter?.Invoke(exporter);
    }

    private static Uri GetEndpoint(BlazorTelemetryClientOptions options, OtlpSignal signal) => signal switch
    {
        OtlpSignal.Logs => options.LogsEndpoint ?? options.Endpoint!,
        OtlpSignal.Traces => options.TracesEndpoint ?? options.Endpoint!,
        OtlpSignal.Metrics => options.MetricsEndpoint ?? options.Endpoint!,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
    };

    private static string? GetHeaders(BlazorTelemetryClientOptions options, OtlpSignal signal) => signal switch
    {
        OtlpSignal.Logs => options.LogsHeaders ?? options.Headers,
        OtlpSignal.Traces => options.TracesHeaders ?? options.Headers,
        OtlpSignal.Metrics => options.MetricsHeaders ?? options.Headers,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
    };

    private static OtlpExportProtocol GetProtocol(BlazorTelemetryClientOptions options, OtlpSignal signal) => signal switch
    {
        OtlpSignal.Logs => options.LogsProtocol ?? options.Protocol,
        OtlpSignal.Traces => options.TracesProtocol ?? options.Protocol,
        OtlpSignal.Metrics => options.MetricsProtocol ?? options.Protocol,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
    };

    private static Uri BuildEndpoint(Uri endpoint, OtlpExportProtocol protocol, OtlpSignal signal)
    {
        if (protocol == OtlpExportProtocol.Grpc)
        {
            return endpoint;
        }

        var signalPath = signal switch
        {
            OtlpSignal.Logs => "v1/logs",
            OtlpSignal.Traces => "v1/traces",
            OtlpSignal.Metrics => "v1/metrics",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
        };
        var path = endpoint.AbsolutePath.TrimEnd('/');
        var signalPathIndex = path.LastIndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
        if (signalPathIndex >= 0)
        {
            var builder = new UriBuilder(endpoint)
            {
                Path = $"{path[..signalPathIndex]}/{signalPath}"
            };
            return builder.Uri;
        }

        var endpointBuilder = new UriBuilder(endpoint)
        {
            Path = $"{path}/{signalPath}"
        };
        return endpointBuilder.Uri;
    }

    private static bool IsIncluded(string? path, BlazorTelemetryClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return !options.ExcludedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static Uri? ReadEndpoint(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ? endpoint : null;
    }

    private static OtlpExportProtocol? ReadProtocol(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.Equals(value, "grpc", StringComparison.OrdinalIgnoreCase))
        {
            return OtlpExportProtocol.Grpc;
        }

        if (string.Equals(value, "http/protobuf", StringComparison.OrdinalIgnoreCase))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        return null;
    }

    private enum OtlpSignal
    {
        Logs,
        Traces,
        Metrics
    }
}
