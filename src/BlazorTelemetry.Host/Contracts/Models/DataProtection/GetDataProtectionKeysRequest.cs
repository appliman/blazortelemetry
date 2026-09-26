using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.DataProtection;

public sealed record GetDataProtectionKeysRequest : IRequest<QueryResult<IReadOnlyList<string>>>;
