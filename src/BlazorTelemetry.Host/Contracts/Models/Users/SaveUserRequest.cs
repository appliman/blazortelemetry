using System.Security.Claims;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.Users;

public sealed record SaveUserRequest(TelemetryUser User, bool ResetSecret, Guid ExpectedVersion, ClaimsPrincipal Principal) : IRequest<QueryResult<string>>;
