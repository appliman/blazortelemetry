using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

public sealed record GetDashboardBreakdownRequest(DateTimeOffset FromUtc, string? ServiceName, string? ExcludedRequestServiceName, DateTimeOffset? ToUtc) : IRequest<QueryResult<DashboardBreakdown>>;
