using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Metrics;

public sealed record GetMetricSeriesRequest(string Name, string? ServiceName, DateTimeOffset FromUtc, DateTimeOffset? ToUtc) : IRequest<QueryResult<IReadOnlyList<MetricSeriesPoint>>>;
