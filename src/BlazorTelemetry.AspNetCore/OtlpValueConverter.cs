using System.Text.Json;
using BlazorTelemetry.Core;
using OpenTelemetry.Proto.Common.V1;

namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpValueConverter(BlazorTelemetryOptions options)
{
    public Dictionary<string, object?> ToDictionary(IEnumerable<KeyValue> attributes)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var attribute in attributes.Take(options.MaximumAttributes))
        {
            result[attribute.Key] = IsSensitive(attribute.Key) ? "[redacted]" : ToObject(attribute.Value);
        }

        return result;
    }

    public string ToJson(IEnumerable<KeyValue> attributes)
    {
        return JsonSerializer.Serialize(ToDictionary(attributes));
    }

    public object? ToObject(AnyValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(value.BytesValue.Span),
            AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue.Values.Select(ToObject).ToArray(),
            AnyValue.ValueOneofCase.KvlistValue => ToDictionary(value.KvlistValue.Values),
            _ => null
        };
    }

    private bool IsSensitive(string key)
    {
        return options.SensitiveAttributePatterns.Any(pattern => key.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
