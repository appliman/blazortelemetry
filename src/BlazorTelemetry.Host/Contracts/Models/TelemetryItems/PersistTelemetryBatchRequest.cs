using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

public sealed record PersistTelemetryBatchRequest(Guid BatchId) : IRequest;
