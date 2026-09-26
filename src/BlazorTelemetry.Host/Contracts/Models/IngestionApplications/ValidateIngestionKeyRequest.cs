using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.IngestionApplications;

public sealed record ValidateIngestionKeyRequest(string KeyHash) : IRequest<QueryResult<IngestionApplication?>>;
