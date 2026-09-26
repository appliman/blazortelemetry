using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Incidents;

public sealed record AcknowledgeIncidentRequest(long Id) : IRequest<CommandResult>;
