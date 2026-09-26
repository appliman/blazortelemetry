using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.NotificationDeliveries;

public sealed record RecordNotificationDeliveryResultRequest(long Id, bool Succeeded, DateTimeOffset NowUtc, string? Error) : IRequest<CommandResult>;
