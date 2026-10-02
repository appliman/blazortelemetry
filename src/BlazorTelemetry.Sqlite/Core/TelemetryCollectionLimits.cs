using System.Text;

namespace BlazorTelemetry.Core;

public static class TelemetryCollectionLimits
{
    private const int MAXIMUM_COLLECTION_ITEMS = 128;
    private const int MAXIMUM_COLLECTION_BYTES = 16 * 1024;

    public static void Apply(TelemetryItem _item)
    {
        _item.CollectionsTruncated |= _item.Events.Count > MAXIMUM_COLLECTION_ITEMS
            || _item.Links.Count > MAXIMUM_COLLECTION_ITEMS || _item.Quantiles.Count > MAXIMUM_COLLECTION_ITEMS
            || _item.Exemplars.Count > MAXIMUM_COLLECTION_ITEMS
            || _item.Buckets.GroupBy(_bucket => _bucket.Group).Any(_group => _group.Count() > MAXIMUM_COLLECTION_ITEMS);
        _item.Events = _item.Events.Take(MAXIMUM_COLLECTION_ITEMS).ToList();
        _item.Links = _item.Links.Take(MAXIMUM_COLLECTION_ITEMS).ToList();
        _item.Quantiles = _item.Quantiles.Take(MAXIMUM_COLLECTION_ITEMS).ToList();
        _item.Exemplars = _item.Exemplars.Take(MAXIMUM_COLLECTION_ITEMS).ToList();
        _item.Buckets = _item.Buckets.GroupBy(_bucket => _bucket.Group).SelectMany(_group => _group.Take(MAXIMUM_COLLECTION_ITEMS)).ToList();
        var _bytes = _item.Events.Sum(_event => 64L + Size(_event.Name) + Size(_event.AttributesJson))
            + _item.Links.Sum(_link => 64L + Size(_link.TraceId) + Size(_link.SpanId) + Size(_link.AttributesJson))
            + _item.Exemplars.Sum(_exemplar => 64L + Size(_exemplar.TraceId) + Size(_exemplar.SpanId) + Size(_exemplar.AttributesJson))
            + _item.Buckets.Count * 24L + _item.Quantiles.Count * 16L;
        if (_bytes > MAXIMUM_COLLECTION_BYTES)
        {
            _item.CollectionsTruncated = true;
            _item.Events.Clear();
            _item.Links.Clear();
            _item.Exemplars.Clear();
            _item.Buckets.Clear();
            _item.Quantiles.Clear();
        }
    }

    private static int Size(string? _value) => _value is null ? 0 : Encoding.UTF8.GetByteCount(_value);
}
