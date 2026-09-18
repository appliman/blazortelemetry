using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BlazorTelemetry.Client;

public sealed class BlazorTelemetryClientOptions
{
    private readonly List<KeyValuePair<string, object>> _resourceAttributes = [];
    private readonly List<string> _sources =
    [
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Components.Server.Circuits"
    ];
    private readonly List<string> _meters =
    [
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Components.Lifecycle",
        "Microsoft.AspNetCore.Components.Server.Circuits"
    ];
    private readonly List<string> _excludedPathPrefixes = ["/health", "/v1/logs", "/v1/traces", "/v1/metrics", "/opentelemetry.proto.collector."];
    private readonly List<string> _requestHeaders =
    [
        "Accept",
        "Accept-Encoding",
        "Accept-Language",
        "Content-Encoding",
        "Content-Length",
        "Content-Type",
        "Origin",
        "Sec-Fetch-Dest",
        "Sec-Fetch-Mode",
        "Sec-Fetch-Site",
        "X-Correlation-ID",
        "X-Request-ID"
    ];
    private readonly List<string> _responseHeaders =
    [
        "Cache-Control",
        "Content-Encoding",
        "Content-Length",
        "Content-Type",
        "ETag",
        "Vary",
        "X-Correlation-ID",
        "X-Request-ID"
    ];

    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
    public Uri? Endpoint { get; set; }
    public Uri? LogsEndpoint { get; set; }
    public Uri? TracesEndpoint { get; set; }
    public Uri? MetricsEndpoint { get; set; }
    public string? Headers { get; set; }
    public string? LogsHeaders { get; set; }
    public string? TracesHeaders { get; set; }
    public string? MetricsHeaders { get; set; }
    public string? DeploymentEnvironment { get; set; }
    public OtlpExportProtocol Protocol { get; set; } = OtlpExportProtocol.Grpc;
    public OtlpExportProtocol? LogsProtocol { get; set; }
    public OtlpExportProtocol? TracesProtocol { get; set; }
    public OtlpExportProtocol? MetricsProtocol { get; set; }
    public bool LogsEnabled { get; set; } = true;
    public bool TracesEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;
    public bool AspNetCoreInstrumentationEnabled { get; set; } = true;
    public bool EntityFrameworkCoreInstrumentationEnabled { get; set; } = true;
    public bool EntityFrameworkMetricsEnabled { get; set; } = true;
    public bool HttpClientInstrumentationEnabled { get; set; } = true;
    public bool RuntimeInstrumentationEnabled { get; set; } = true;
    public bool CaptureDatabaseStatements { get; set; }
    public bool RecordExceptions { get; set; } = true;
    public bool IncludeFormattedLogMessage { get; set; } = true;
    public bool IncludeLogScopes { get; set; } = true;
    public bool ParseLogStateValues { get; set; } = true;
    public bool CaptureClientAddress { get; set; } = true;
    public bool CaptureNetworkAddresses { get; set; } = true;
    public bool CaptureHttpHeaders { get; set; } = true;
    public bool CaptureHttpBodySizes { get; set; } = true;
    public int MaximumAttributeLength { get; set; } = 2048;
    public Func<HttpContext, bool>? AspNetCoreRequestFilter { get; set; }
    public Func<HttpRequestMessage, bool>? HttpClientRequestFilter { get; set; }
    public Action<Activity, HttpRequest>? EnrichAspNetCoreRequest { get; set; }
    public Action<Activity, HttpResponse>? EnrichAspNetCoreResponse { get; set; }
    public Action<Activity, Exception>? EnrichAspNetCoreException { get; set; }
    public Action<Activity, HttpRequestMessage>? EnrichHttpClientRequest { get; set; }
    public Action<Activity, HttpResponseMessage>? EnrichHttpClientResponse { get; set; }
    public Action<Activity, Exception>? EnrichHttpClientException { get; set; }
    public Action<EntityFrameworkInstrumentationOptions>? ConfigureEntityFrameworkCore { get; set; }
    public Action<OtlpExporterOptions>? ConfigureExporter { get; set; }
    public Action<ResourceBuilder>? ConfigureResource { get; set; }
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }
    public Action<OpenTelemetryLoggerOptions>? ConfigureLogging { get; set; }
    public IReadOnlyList<KeyValuePair<string, object>> ResourceAttributes => _resourceAttributes;
    public IReadOnlyList<string> Sources => _sources;
    public IReadOnlyList<string> Meters => _meters;
    public IReadOnlyList<string> ExcludedPathPrefixes => _excludedPathPrefixes;
    public IReadOnlyList<string> RequestHeaders => _requestHeaders;
    public IReadOnlyList<string> ResponseHeaders => _responseHeaders;

    public void AddResourceAttribute(string name, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _resourceAttributes.Add(new KeyValuePair<string, object>(name, value));
    }

    public void AddSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _sources.Add(source);
    }

    public void AddMeter(string meter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meter);
        _meters.Add(meter);
    }

    public void ExcludePath(string pathPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathPrefix);
        _excludedPathPrefixes.Add(pathPrefix.StartsWith('/') ? pathPrefix : $"/{pathPrefix}");
    }

    public void CaptureRequestHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        _requestHeaders.Add(headerName);
    }

    public void CaptureResponseHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        _responseHeaders.Add(headerName);
    }
}
