using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Host.Contracts.Models.Users;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.Users;

public sealed class GetUsersRequestHandler(IDbContextFactory<TelemetryDbContext> factory, UserAuthenticationService authentication,
    ILogger<GetUsersRequestHandler> logger) : IRequestHandler<GetUsersRequest, QueryResult<IReadOnlyList<TelemetryUser>>>
{
    public async Task<QueryResult<IReadOnlyList<TelemetryUser>>> Handle(GetUsersRequest request, CancellationToken cancellationToken)
    {
        if (!authentication.IsAdministrator(request.Principal))
        {
            return new() { BrokenRules = [new("forbidden", "", "Administrator access is required.")] };
        }
        try
        {
            await using var _context = await factory.CreateDbContextAsync(cancellationToken);
            var _users = await _context.Users.AsNoTracking().OrderBy(_user => _user.Identifier).ToListAsync(cancellationToken);
            foreach (var _user in _users)
            {
                _user.ProtectedSecret = string.Empty;
            }
            return new() { Data = _users };
        }
        catch (Exception _exception)
        {
            logger.LogError(_exception, "Loading users failed.");
            return new() { BrokenRules = [new("storage_error", "", "The operation failed.")] };
        }
    }
}
