using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Maintenance;

public sealed record PurgeRequest(DateTimeOffset RawBeforeUtc, DateTimeOffset MetricsBeforeUtc, long MaximumBytes) : IRequest<CommandResult>;
