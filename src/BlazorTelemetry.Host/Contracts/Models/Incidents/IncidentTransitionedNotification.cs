using BlazorTelemetry.Core;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Incidents;

public sealed record IncidentTransitionedNotification(long IncidentId, IncidentState State) : INotification;
