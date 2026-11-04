using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.IngestionApplications;

public sealed record SetIngestionApplicationAutoLoginRequest(int Id, bool AllowAutoLogin) : IRequest<CommandResult>;
