using System.Text.Json;
using BlazorTelemetry.Core;
using OpenTelemetry.Proto.Common.V1;

namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpValueConverter(BlazorTelemetryOptions options)
{
    private const int MAXIMUM_ATTRIBUTES_PER_ITEM = 128;
    private const int MAXIMUM_COLLECTION_ITEMS = 128;
    private const int MAXIMUM_VALUE_DEPTH = 8;
    private const int MAXIMUM_VALUE_LENGTH = 2_048;

    internal int MaximumCollectionItems => MAXIMUM_COLLECTION_ITEMS;

    public Dictionary<string, object?> ToDictionary(IEnumerable<KeyValue> attributes)
    {
        return ToDictionary(attributes, 0);
    }

    private Dictionary<string, object?> ToDictionary(IEnumerable<KeyValue> attributes, int depth)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var maximumAttributes = Math.Clamp(options.MaximumAttributes, 0, MAXIMUM_ATTRIBUTES_PER_ITEM);
        foreach (var attribute in attributes.Take(maximumAttributes))
        {
            result[attribute.Key] = IsSensitive(attribute.Key) ? "[redacted]" : ToObject(attribute.Value, depth + 1);
        }

        return result;
    }

    public string ToJson(IEnumerable<KeyValue> attributes)
    {
        return JsonSerializer.Serialize(ToDictionary(attributes));
    }

    public object? ToObject(AnyValue? value)
    {
        return ToObject(value, 0);
    }

    private object? ToObject(AnyValue? value, int depth)
    {
        if (value is null)
        {
            return null;
        }

        if (depth >= MAXIMUM_VALUE_DEPTH)
        {
            return "[maximum depth reached]";
        }

        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => Truncate(value.StringValue),
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.BytesValue => ConvertBytes(value.BytesValue.Span),
            AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue.Values
                .Take(MAXIMUM_COLLECTION_ITEMS)
                .Select(item => ToObject(item, depth + 1))
                .ToArray(),
            AnyValue.ValueOneofCase.KvlistValue => ToDictionary(value.KvlistValue.Values, depth + 1),
            _ => null
        };
    }

    private static string ConvertBytes(ReadOnlySpan<byte> bytes)
    {
        var maximumBytes = (MAXIMUM_VALUE_LENGTH * 3) / 4;
        var length = Math.Min(bytes.Length, maximumBytes);
        var encoded = Convert.ToBase64String(bytes[..length]);
        return bytes.Length == length ? encoded : $"{encoded}…";
    }

    private static string Truncate(string value)
    {
        return value.Length <= MAXIMUM_VALUE_LENGTH ? value : value[..MAXIMUM_VALUE_LENGTH];
    }

    private bool IsSensitive(string key)
    {
        return options.SensitiveAttributePatterns.Any(pattern => key.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
