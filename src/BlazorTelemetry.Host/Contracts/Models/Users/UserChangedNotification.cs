using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Users;

public sealed record UserChangedNotification(long Id, bool IsNewEntity) : INotification;
