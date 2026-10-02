using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorTelemetry.Core;

public static class TelemetryNormalization
{
    public static TelemetryResource Resource(TelemetryItem _item)
    {
        var _attributes = Read(_item.ResourceAttributesJson);
        var _resource = new TelemetryResource
        {
            ServiceName = Extract(_attributes, "service.name") ?? _item.ServiceName,
            ServiceVersion = Extract(_attributes, "service.version") ?? _item.ServiceVersion,
            Environment = Extract(_attributes, "deployment.environment.name", "deployment.environment") ?? _item.Environment,
            ServiceInstanceId = Extract(_attributes, "service.instance.id") ?? _item.ServiceInstanceId,
            SdkName = Extract(_attributes, "telemetry.sdk.name") ?? _item.SdkName,
            SdkLanguage = Extract(_attributes, "telemetry.sdk.language") ?? _item.SdkLanguage,
            SdkVersion = Extract(_attributes, "telemetry.sdk.version") ?? _item.SdkVersion,
            AttributesJson = Canonical(JsonSerializer.Serialize(_attributes))
        };
        _resource.Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ResourceKey(_resource))));
        return _resource;
    }

    public static bool SameResource(TelemetryResource _left, TelemetryResource _right) => ReferenceEquals(_left, _right)
        || (_left.ServiceName == _right.ServiceName && _left.ServiceVersion == _right.ServiceVersion
            && _left.Environment == _right.Environment && _left.ServiceInstanceId == _right.ServiceInstanceId
            && _left.SdkName == _right.SdkName && _left.SdkLanguage == _right.SdkLanguage
            && _left.SdkVersion == _right.SdkVersion && _left.AttributesJson == _right.AttributesJson);

    public static void Validate(TelemetryItem _item)
    {
        var _values = new[] { _item.NumericValue, _item.DurationMs, _item.Sum, _item.Minimum, _item.Maximum }
            .Concat(_item.Buckets.Select(_bucket => _bucket.UpperBound))
            .Concat(_item.Quantiles.SelectMany(_quantile => new double?[] { _quantile.Quantile, _quantile.Value }))
            .Concat(_item.Exemplars.Select(_exemplar => (double?)_exemplar.Value));
        if (_values.Any(_value => _value.HasValue && !double.IsFinite(_value.Value))
            || _item.Count < 0 || _item.ZeroCount < 0 || _item.Buckets.Any(_bucket => _bucket.Count < 0))
        {
            throw new OverflowException("Telemetry numeric value cannot be represented safely.");
        }
    }

    public static void Request(TelemetryItem _item)
    {
        if (_item.Kind != TelemetryKind.Request)
        {
            return;
        }
        Dictionary<string, JsonElement> _attributes;
        try
        {
            _attributes = Read(_item.AttributesJson);
        }
        catch (JsonException)
        {
            // Preserve custom invalid attributes, without treating them as HTTP context.
            _item.HttpMethod ??= _item.Body;
            _item.Url ??= _item.Name;
            return;
        }
        _item.HttpMethod = ExtractRequest(_attributes, _item.HttpMethod, "http.request.method", "http.method") ?? _item.Body;
        _item.Url = ExtractRequest(_attributes, _item.Url, "url.full", "http.url") ?? _item.Name;
        _item.Route = ExtractRequest(_attributes, _item.Route, "http.route", "url.path", "http.target", "RequestPath");
        _item.ProtocolVersion = ExtractRequest(_attributes, _item.ProtocolVersion, "network.protocol.version", "http.flavor", "http.protocol");
        _item.UrlScheme = ExtractRequest(_attributes, _item.UrlScheme, "url.scheme", "http.scheme", "server.scheme");
        _item.ClientAddress = ExtractRequest(_attributes, _item.ClientAddress, "client.address", "network.peer.address", "http.client_ip", "net.sock.peer.addr", "net.peer.ip");
        _item.ServerAddress = ExtractRequest(_attributes, _item.ServerAddress, "server.address", "http.host", "network.local.address", "net.host.name");
        _item.UserAgent = ExtractRequest(_attributes, _item.UserAgent, "user_agent.original", "http.user_agent");
        _item.CircuitId = ExtractRequest(_attributes, _item.CircuitId, "blazor.circuit.id");
        _item.ClientPort = ExtractPort(_attributes, _item.ClientPort, "client.port", "network.peer.port", "net.peer.port");
        _item.ServerPort = ExtractPort(_attributes, _item.ServerPort, "server.port", "network.local.port", "net.host.port");
        _item.StatusCode = ExtractInteger(_attributes, _item.StatusCode, "http.response.status_code", "http.status_code");
        _item.AttributesJson = Canonical(JsonSerializer.Serialize(_attributes));
    }

    public static string Canonical(string _json)
    {
        using var _document = JsonDocument.Parse(_json);
        using var _stream = new MemoryStream();
        using (var _writer = new Utf8JsonWriter(_stream))
        {
            WriteCanonical(_writer, _document.RootElement);
        }
        return Encoding.UTF8.GetString(_stream.ToArray());
    }

    private static string ResourceKey(TelemetryResource _resource) => JsonSerializer.Serialize(new[]
    {
        _resource.ServiceName, _resource.ServiceVersion, _resource.Environment, _resource.ServiceInstanceId,
        _resource.SdkName, _resource.SdkLanguage, _resource.SdkVersion, _resource.AttributesJson
    });

    private static Dictionary<string, JsonElement> Read(string _json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(_json)
        ?? throw new InvalidOperationException("Telemetry attributes must be an object.");

    private static string? Extract(Dictionary<string, JsonElement> _attributes, params string[] _names) => ExtractRequest(_attributes, null, _names);

    private static string? ExtractRequest(Dictionary<string, JsonElement> _attributes, string? _current, params string[] _names)
    {
        var _result = _current;
        foreach (var _name in _names)
        {
            if (!_attributes.TryGetValue(_name, out var _value) || _value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var _text = _value.GetString();
            if (string.IsNullOrWhiteSpace(_text) || _text.Trim() == "—")
            {
                continue;
            }
            _result ??= _text;
            if (_text == _result)
            {
                _attributes.Remove(_name);
            }
        }
        return _result;
    }

    private static int? ExtractPort(Dictionary<string, JsonElement> _attributes, int? _current, params string[] _names)
    {
        var _result = _current;
        foreach (var _name in _names)
        {
            if (!_attributes.TryGetValue(_name, out var _value)
                || !int.TryParse(_value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var _port)
                || _port is < 0 or > 65535)
            {
                continue;
            }
            _result ??= _port;
            if (_port == _result)
            {
                _attributes.Remove(_name);
            }
        }
        return _result;
    }

    private static int? ExtractInteger(Dictionary<string, JsonElement> _attributes, int? _current, params string[] _names)
    {
        var _result = _current;
        foreach (var _name in _names)
        {
            if (!_attributes.TryGetValue(_name, out var _value)
                || !int.TryParse(_value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var _number))
            {
                continue;
            }
            _result ??= _number;
            if (_number == _result)
            {
                _attributes.Remove(_name);
            }
        }
        return _result;
    }

    private static void WriteCanonical(Utf8JsonWriter _writer, JsonElement _element)
    {
        if (_element.ValueKind == JsonValueKind.Object)
        {
            _writer.WriteStartObject();
            foreach (var _property in _element.EnumerateObject().OrderBy(_property => _property.Name, StringComparer.Ordinal))
            {
                _writer.WritePropertyName(_property.Name);
                WriteCanonical(_writer, _property.Value);
            }
            _writer.WriteEndObject();
        }
        else if (_element.ValueKind == JsonValueKind.Array)
        {
            _writer.WriteStartArray();
            foreach (var _value in _element.EnumerateArray())
            {
                WriteCanonical(_writer, _value);
            }
            _writer.WriteEndArray();
        }
        else
        {
            _element.WriteTo(_writer);
        }
    }
}
