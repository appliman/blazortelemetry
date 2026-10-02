using System.Collections.Concurrent;
using System.Security.Claims;

namespace BlazorTelemetry.Host.Authentication;

public sealed class Blazor2faAuthenticationTicketStore(TimeProvider timeProvider)
{
    private const int MAXIMUM_TICKETS = 1_024;
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, AuthenticationTicket> tickets = new();
    private long _issueSequence;

    public Guid Issue(IReadOnlyCollection<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var now = timeProvider.GetUtcNow();
        RemoveExpired(now);
        var token = Guid.NewGuid();
        tickets[token] = new AuthenticationTicket(
            claims.ToArray(),
            Interlocked.Increment(ref _issueSequence),
            now.Add(TicketLifetime));
        TrimToMaximum();
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
            RemoveExpired(timeProvider.GetUtcNow());
            return false;
        }

        claims = ticket.Claims;
        return true;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var entry in tickets)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                tickets.TryRemove(entry.Key, out _);
            }
        }
    }

    private void TrimToMaximum()
    {
        while (tickets.Count > MAXIMUM_TICKETS)
        {
            var oldest = tickets.MinBy(entry => entry.Value.Sequence);
            if (oldest.Key == Guid.Empty || !tickets.TryRemove(oldest.Key, out _))
            {
                return;
            }
        }
    }

    private sealed record AuthenticationTicket(
        IReadOnlyCollection<Claim> Claims,
        long Sequence,
        DateTimeOffset ExpiresAt);
}
