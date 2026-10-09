using System.Security.Claims;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Users;

public sealed record GetUsersRequest(ClaimsPrincipal Principal) : IRequest<QueryResult<IReadOnlyList<TelemetryUser>>>;
