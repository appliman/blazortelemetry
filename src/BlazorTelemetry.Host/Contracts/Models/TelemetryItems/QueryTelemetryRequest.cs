using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

public sealed record QueryTelemetryRequest(TelemetryQuery Query) : IRequest<QueryResult<TelemetryPage>>;
