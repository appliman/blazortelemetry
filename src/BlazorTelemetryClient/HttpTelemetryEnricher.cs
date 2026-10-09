using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace BlazorTelemetry.Client;

internal static class HttpTelemetryEnricher
{
    public static void EnrichRequest(Activity activity, HttpRequest request, BlazorTelemetryClientOptions options)
    {
        var connection = request.HttpContext.Connection;
        activity.SetTag("http.request.id", request.HttpContext.TraceIdentifier);
        activity.SetTag("aspnetcore.request.path_base", EmptyToNull(request.PathBase.Value));

        if (options.CaptureNetworkAddresses)
        {
            activity.SetTag("network.peer.address", connection.RemoteIpAddress?.ToString());
            activity.SetTag("network.peer.port", PositiveOrNull(connection.RemotePort));
            activity.SetTag("network.local.address", connection.LocalIpAddress?.ToString());
            activity.SetTag("network.local.port", PositiveOrNull(connection.LocalPort));
        }

        if (options.CaptureHttpBodySizes)
        {
            activity.SetTag("http.request.body.size", request.ContentLength);
        }

        if (options.CaptureHttpHeaders)
        {
            CaptureHeaders(activity, "http.request.header", request.Headers, options.RequestHeaders, options.MaximumAttributeLength);
        }

        options.EnrichAspNetCoreRequest?.Invoke(activity, request);
    }

    public static void EnrichResponse(Activity activity, HttpResponse response, BlazorTelemetryClientOptions options)
    {
        var context = response.HttpContext;

        if (options.CaptureClientAddress)
        {
            var (_address, _port) = GetClientAddress(context);
            activity.SetTag("client.address", _address);
            activity.SetTag("client.port", _port);
        }

        activity.SetTag("http.request.forwarded", HasForwardedHeaders(context.Request));

        if (options.CaptureHttpBodySizes)
        {
            activity.SetTag("http.response.body.size", response.ContentLength);
        }

        if (options.CaptureHttpHeaders)
        {
            CaptureHeaders(activity, "http.response.header", response.Headers, options.ResponseHeaders, options.MaximumAttributeLength);
        }

        options.EnrichAspNetCoreResponse?.Invoke(activity, response);
    }

    public static void EnrichException(Activity activity, Exception exception, BlazorTelemetryClientOptions options)
    {
        activity.SetTag("error.type", exception.GetType().FullName);
        options.EnrichAspNetCoreException?.Invoke(activity, exception);
    }

    public static void EnrichRequest(Activity activity, HttpRequestMessage request, BlazorTelemetryClientOptions options)
    {
        activity.SetTag("http.request.version", request.Version.ToString());

        if (options.CaptureHttpBodySizes)
        {
            activity.SetTag("http.request.body.size", request.Content?.Headers.ContentLength);
        }

        if (options.CaptureHttpHeaders)
        {
            CaptureHeaders(activity, "http.request.header", request.Headers, request.Content?.Headers, options.RequestHeaders, options.MaximumAttributeLength);
        }

        options.EnrichHttpClientRequest?.Invoke(activity, request);
    }

    public static void EnrichResponse(Activity activity, HttpResponseMessage response, BlazorTelemetryClientOptions options)
    {
        activity.SetTag("http.response.version", response.Version.ToString());

        if (options.CaptureHttpBodySizes)
        {
            activity.SetTag("http.response.body.size", response.Content.Headers.ContentLength);
        }

        if (options.CaptureHttpHeaders)
        {
            CaptureHeaders(activity, "http.response.header", response.Headers, response.Content.Headers, options.ResponseHeaders, options.MaximumAttributeLength);
        }

        options.EnrichHttpClientResponse?.Invoke(activity, response);
    }

    public static void EnrichHttpClientException(Activity activity, Exception exception, BlazorTelemetryClientOptions options)
    {
        activity.SetTag("error.type", exception.GetType().FullName);
        options.EnrichHttpClientException?.Invoke(activity, exception);
    }

    private static (string? Address, int? Port) GetClientAddress(HttpContext _context)
    {
        var _connection = _context.Connection;
        var _forwardedFor = _context.Request.Headers["X-Forwarded-For"];
        if (_forwardedFor.Count > 0)
        {
            // Use the original client's first entry for telemetry only, without changing proxy trust or the connection.
            var _value = _forwardedFor[0].AsSpan();
            var _separator = _value.IndexOf(',');
            if (_separator >= 0)
            {
                _value = _value[.._separator];
            }

            _value = _value.Trim();
            if (IPEndPoint.TryParse(_value, out var _endpoint))
            {
                // The connection port belongs to the proxy unless the connection already identifies this client.
                var _port = PositiveOrNull(_endpoint.Port)
                    ?? (_endpoint.Address.Equals(_connection.RemoteIpAddress) ? PositiveOrNull(_connection.RemotePort) : null);
                return (_endpoint.Address.ToString(), _port);
            }
        }

        // Forwarded Headers Middleware may already have consumed the header and updated the connection.
        return (_connection.RemoteIpAddress?.ToString(), PositiveOrNull(_connection.RemotePort));
    }

    private static void CaptureHeaders(
        Activity activity,
        string prefix,
        IHeaderDictionary headers,
        IEnumerable<string> names,
        int maximumLength)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (headers.TryGetValue(name, out var values))
            {
                SetHeaderTag(activity, prefix, name, values, maximumLength);
            }
        }
    }

    private static void CaptureHeaders(
        Activity activity,
        string prefix,
        HttpHeaders headers,
        HttpContentHeaders? contentHeaders,
        IEnumerable<string> names,
        int maximumLength)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (headers.TryGetValues(name, out var values) || contentHeaders?.TryGetValues(name, out values) == true)
            {
                SetHeaderTag(activity, prefix, name, values, maximumLength);
            }
        }
    }

    private static void SetHeaderTag(
        Activity activity,
        string prefix,
        string name,
        IEnumerable<string> values,
        int maximumLength)
    {
        var value = string.Join(", ", values);
        if (value.Length > maximumLength)
        {
            value = value[..maximumLength];
        }

        activity.SetTag($"{prefix}.{name.ToLowerInvariant()}", value);
    }

    private static bool HasForwardedHeaders(HttpRequest request) =>
        request.Headers.ContainsKey("Forwarded")
        || request.Headers.ContainsKey("X-Forwarded-For")
        || request.Headers.ContainsKey("X-Forwarded-Host")
        || request.Headers.ContainsKey("X-Forwarded-Proto");

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? PositiveOrNull(int value) => value > 0 ? value : null;
}
