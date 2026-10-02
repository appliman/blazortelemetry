using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Sqlite;

public sealed partial class TelemetryDbContext
{
    public async Task<int> StoreTelemetry(IReadOnlyCollection<TelemetryItem> _items, CancellationToken _cancellationToken)
    {
        await using var _transaction = await Database.BeginTransactionAsync(_cancellationToken);
        var _inserted = 0;
        foreach (var _chunk in _items.Chunk(500))
        {
            var _inputs = new Dictionary<(string, string?, string?, string?, string?, string?, string?, string), TelemetryResource>();
            var _resources = _chunk.Select(_item =>
            {
                var _key = (_item.ServiceName, _item.ServiceVersion, _item.Environment, _item.ServiceInstanceId, _item.SdkName, _item.SdkLanguage, _item.SdkVersion, _item.ResourceAttributesJson);
                if (!_inputs.TryGetValue(_key, out var _resource))
                {
                    _resource = TelemetryNormalization.Resource(_item);
                    _inputs.Add(_key, _resource);
                }
                return _resource;
            }).ToArray();
            var _hashes = _resources.Select(_resource => _resource.Hash).Distinct().ToArray();
            var _candidates = await TelemetryResources.Where(_resource => _hashes.Contains(_resource.Hash)).ToListAsync(_cancellationToken);
            for (var _index = 0; _index < _chunk.Length; _index++)
            {
                var _resource = _resources[_index];
                var _existing = _candidates.FirstOrDefault(_candidate => _candidate.Hash == _resource.Hash && TelemetryNormalization.SameResource(_candidate, _resource));
                if (_existing is null)
                {
                    TelemetryResources.Add(_resource);
                    _candidates.Add(_resource);
                }
                else
                {
                    _resources[_index] = _existing;
                }
            }
            await SaveChangesAsync(_cancellationToken);
            var _pending = new List<Action>();
            foreach (var _group in _chunk.Select((_item, _index) => (_item, _resource: _resources[_index])).GroupBy(_pair => _pair._item.Kind))
            {
                var _fingerprints = _group.Select(_pair => _pair._item.Fingerprint).Where(_value => _value is not null).Cast<string>().Distinct().ToArray();
                var _existing = (await QueryTelemetry(_group.Key).Where(_item => _item.Fingerprint != null && _fingerprints.Contains(_item.Fingerprint)).Select(_item => _item.Fingerprint!).ToListAsync(_cancellationToken)).ToHashSet(StringComparer.Ordinal);
                foreach (var (_item, _resource) in _group)
                {
                    if (_item.Fingerprint is not null && !_existing.Add(_item.Fingerprint))
                    {
                        continue;
                    }
                    TelemetryNormalization.Validate(_item);
                    TelemetryCollectionLimits.Apply(_item);
                    TelemetryNormalization.Request(_item);
                    if (_item.Kind != TelemetryKind.Request)
                    {
                        try
                        {
                            _item.AttributesJson = TelemetryNormalization.Canonical(_item.AttributesJson);
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            // Retain custom malformed attributes without losing the event.
                        }
                    }
                    _item.ResourceId = _resource.Id;
                    switch (_item.Kind)
                    {
                        case TelemetryKind.Log:
                        {
                            var _record = new TelemetryLog
                            {
                                Fingerprint = _item.Fingerprint,
                                ResourceId = _item.ResourceId,
                                TimestampUtc = _item.TimestampUtc.ToUniversalTime(),
                                ObservedUtc = _item.ObservedUtc.ToUniversalTime(),
                                Name = _item.Name,
                                ScopeName = _item.ScopeName,
                                ScopeVersion = _item.ScopeVersion,
                                AttributesJson = _item.AttributesJson,
                                Body = _item.Body,
                                SeverityText = _item.SeverityText,
                                SeverityNumber = _item.SeverityNumber,
                                TraceId = _item.TraceId,
                                SpanId = _item.SpanId,
                                Flags = _item.Flags,
                                DroppedAttributesCount = _item.DroppedAttributesCount,
                            };
                            Add(_record);
                            _pending.Add(() =>
                            {
                                _item.Id = _record.Id;
                            });
                            break;
                        }
                        case TelemetryKind.Trace:
                        {
                            var _record = new TelemetryTrace
                            {
                                CollectionsTruncated = _item.CollectionsTruncated,
                                ResourceId = _item.ResourceId,
                                TimestampUtc = _item.TimestampUtc.ToUniversalTime(),
                                ObservedUtc = _item.ObservedUtc.ToUniversalTime(),
                                Name = _item.Name,
                                ScopeName = _item.ScopeName,
                                ScopeVersion = _item.ScopeVersion,
                                AttributesJson = _item.AttributesJson,
                                TraceId = _item.TraceId,
                                SpanId = _item.SpanId,
                                ParentSpanId = _item.ParentSpanId,
                                DurationMs = _item.DurationMs,
                                StatusCode = _item.StatusCode,
                                Body = _item.Body,
                                SpanKind = _item.SpanKind,
                                Fingerprint = _item.Fingerprint,
                            };
                            Add(_record);
                            _pending.Add(() =>
                            {
                                _item.Id = _record.Id;
                                for (var _ordinal = 0; _ordinal < _item.Events.Count; _ordinal++)
                                {
                                    var _child = _item.Events[_ordinal];
                                    _child.TraceRecordId = _record.Id;
                                    _child.Ordinal = _ordinal;
                                    Add(_child);
                                }
                                for (var _ordinal = 0; _ordinal < _item.Links.Count; _ordinal++)
                                {
                                    var _child = _item.Links[_ordinal];
                                    _child.TraceRecordId = _record.Id;
                                    _child.Ordinal = _ordinal;
                                    Add(_child);
                                }
                            });
                            break;
                        }
                        case TelemetryKind.Metric:
                        {
                            var _record = new TelemetryMetric
                            {
                                CollectionsTruncated = _item.CollectionsTruncated,
                                ResourceId = _item.ResourceId,
                                TimestampUtc = _item.TimestampUtc.ToUniversalTime(),
                                ObservedUtc = _item.ObservedUtc.ToUniversalTime(),
                                Name = _item.Name,
                                ScopeName = _item.ScopeName,
                                ScopeVersion = _item.ScopeVersion,
                                AttributesJson = _item.AttributesJson,
                                Body = _item.Body,
                                Unit = _item.Unit,
                                MetricType = _item.MetricType,
                                NumericValue = _item.NumericValue,
                                StartTimeUtc = _item.StartTimeUtc,
                                AggregationTemporality = _item.AggregationTemporality,
                                IsMonotonic = _item.IsMonotonic,
                                Count = _item.Count,
                                Sum = _item.Sum,
                                Minimum = _item.Minimum,
                                Maximum = _item.Maximum,
                                Scale = _item.Scale,
                                ZeroCount = _item.ZeroCount,
                                PositiveOffset = _item.PositiveOffset,
                                NegativeOffset = _item.NegativeOffset,
                                Fingerprint = _item.Fingerprint,
                            };
                            Add(_record);
                            _pending.Add(() =>
                            {
                                _item.Id = _record.Id;
                                for (var _ordinal = 0; _ordinal < _item.Buckets.Count; _ordinal++)
                                {
                                    var _child = _item.Buckets[_ordinal];
                                    _child.MetricId = _record.Id;
                                    Add(_child);
                                }
                                for (var _ordinal = 0; _ordinal < _item.Quantiles.Count; _ordinal++)
                                {
                                    var _child = _item.Quantiles[_ordinal];
                                    _child.MetricId = _record.Id;
                                    _child.Ordinal = _ordinal;
                                    Add(_child);
                                }
                                for (var _ordinal = 0; _ordinal < _item.Exemplars.Count; _ordinal++)
                                {
                                    var _child = _item.Exemplars[_ordinal];
                                    _child.MetricId = _record.Id;
                                    _child.Ordinal = _ordinal;
                                    Add(_child);
                                }
                            });
                            break;
                        }
                        case TelemetryKind.Request:
                        {
                            var _record = new TelemetryRequest
                            {
                                ResourceId = _item.ResourceId,
                                TimestampUtc = _item.TimestampUtc.ToUniversalTime(),
                                ObservedUtc = _item.ObservedUtc.ToUniversalTime(),
                                Name = _item.Name,
                                ScopeName = _item.ScopeName,
                                ScopeVersion = _item.ScopeVersion,
                                AttributesJson = _item.AttributesJson,
                                TraceId = _item.TraceId,
                                SpanId = _item.SpanId,
                                ParentSpanId = _item.ParentSpanId,
                                DurationMs = _item.DurationMs,
                                StatusCode = _item.StatusCode,
                                SpanKind = _item.SpanKind,
                                Source = _item.Source,
                                CircuitId = _item.CircuitId,
                                HttpMethod = _item.HttpMethod,
                                Url = _item.Url,
                                Route = _item.Route,
                                ProtocolVersion = _item.ProtocolVersion,
                                UrlScheme = _item.UrlScheme,
                                ClientAddress = _item.ClientAddress,
                                ClientPort = _item.ClientPort,
                                ServerAddress = _item.ServerAddress,
                                ServerPort = _item.ServerPort,
                                UserAgent = _item.UserAgent,
                                Fingerprint = _item.Fingerprint,
                            };
                            Add(_record);
                            _pending.Add(() =>
                            {
                                _item.Id = _record.Id;
                            });
                            break;
                        }
                        default:
                            throw new InvalidOperationException("Unsupported telemetry category.");
                    }
                    _inserted++;
                }
            }
            await SaveChangesAsync(_cancellationToken);
            foreach (var _attach in _pending)
            {
                _attach();
            }
            await SaveChangesAsync(_cancellationToken);
            var _resourceIds = _resources.Select(_resource => _resource.Id).Distinct().ToArray();
            await DeleteOrphanResources(_cancellationToken, _resourceIds);
            ChangeTracker.Clear();
        }
        await _transaction.CommitAsync(_cancellationToken);
        return _inserted;
    }

    public Task<int> DeleteOrphanResources(CancellationToken _cancellationToken, long[]? _ids = null) => TelemetryResources
        .Where(_resource => _ids == null || _ids.Contains(_resource.Id))
        .Where(_resource => !TelemetryLogs.Any(_item => _item.ResourceId == _resource.Id)
            && !TelemetryTraces.Any(_item => _item.ResourceId == _resource.Id)
            && !TelemetryMetrics.Any(_item => _item.ResourceId == _resource.Id)
            && !TelemetryRequest.Any(_item => _item.ResourceId == _resource.Id))
        .ExecuteDeleteAsync(_cancellationToken);
}
