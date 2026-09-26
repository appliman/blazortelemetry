using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Metrics;

public sealed record GetBlazorDashboardMetricsRequest(DateTimeOffset FromUtc, string? ServiceName, DateTimeOffset? ToUtc) : IRequest<QueryResult<BlazorDashboardMetrics>>;
