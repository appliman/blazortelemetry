using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.IngestionApplications;

public sealed record GetIngestionApplicationRequest(int Id) : IRequest<QueryResult<IngestionApplication?>>;
