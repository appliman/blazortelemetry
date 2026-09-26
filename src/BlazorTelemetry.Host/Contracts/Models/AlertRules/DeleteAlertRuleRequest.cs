using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.AlertRules;

public sealed record DeleteAlertRuleRequest(int Id) : IRequest<CommandResult>;
