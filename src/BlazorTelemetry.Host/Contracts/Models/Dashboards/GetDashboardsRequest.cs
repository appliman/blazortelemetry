using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Dashboards;

public sealed record GetDashboardsRequest() : IRequest<QueryResult<IReadOnlyList<DashboardDefinition>>>;
