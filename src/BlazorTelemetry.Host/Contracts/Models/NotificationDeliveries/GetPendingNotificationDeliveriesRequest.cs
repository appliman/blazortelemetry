using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.NotificationDeliveries;

public sealed record GetPendingNotificationDeliveriesRequest(DateTimeOffset NowUtc) : IRequest<QueryResult<IReadOnlyList<NotificationDelivery>>>;
