using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Metrics;

public sealed record GetMetricNamesRequest(DateTimeOffset FromUtc, DateTimeOffset ToUtc, string? ServiceName) : IRequest<QueryResult<IReadOnlyList<string>>>;
