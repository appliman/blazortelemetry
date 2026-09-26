using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Dashboards;

public sealed record SaveDashboardRequest(DashboardDefinition Dashboard) : IRequest<PersistResult>;
