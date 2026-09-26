using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.IngestionApplications;

public sealed record ValidateMcpReadKeyRequest(string KeyHash) : IRequest<QueryResult<IngestionApplication?>>;
