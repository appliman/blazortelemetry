using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Maintenance;

public sealed record InitializeTelemetryDatabaseRequest : IRequest<CommandResult>;
