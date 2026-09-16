using System.Collections.Concurrent;
using System.Security.Claims;

namespace Blazor2fa;

public sealed class Blazor2faAuthenticationTicketStore(TimeProvider timeProvider)
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, AuthenticationTicket> tickets = new();

    public Guid Issue(IReadOnlyCollection<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var token = Guid.NewGuid();
        tickets[token] = new AuthenticationTicket(
            claims.ToArray(),
            timeProvider.GetUtcNow().Add(TicketLifetime));
        return token;
    }

    public bool TryConsume(string token, out IReadOnlyCollection<Claim> claims)
    {
        claims = [];
        if (!Guid.TryParse(token, out var parsedToken)
            || parsedToken == Guid.Empty
            || !tickets.TryRemove(parsedToken, out var ticket)
            || ticket.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return false;
        }

        claims = ticket.Claims;
        return true;
    }

    private sealed record AuthenticationTicket(
        IReadOnlyCollection<Claim> Claims,
        DateTimeOffset ExpiresAt);
}
