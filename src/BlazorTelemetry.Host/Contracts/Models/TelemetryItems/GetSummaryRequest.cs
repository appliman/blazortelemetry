using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

public sealed record GetSummaryRequest(DateTimeOffset FromUtc, DateTimeOffset? ToUtc, string? ServiceName) : IRequest<QueryResult<TelemetrySummary>>;
