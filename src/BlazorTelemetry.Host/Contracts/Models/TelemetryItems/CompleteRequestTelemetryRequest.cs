using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

public sealed record CompleteRequestTelemetryRequest(string RequestId, double DurationMs, int StatusCode) : IRequest<CommandResult>;
