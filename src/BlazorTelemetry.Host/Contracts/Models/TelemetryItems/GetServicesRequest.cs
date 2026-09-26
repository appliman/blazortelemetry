using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

public sealed record GetServicesRequest(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IRequest<QueryResult<IReadOnlyList<string>>>;
