using System.Security.Claims;
using Blazor2fa;

namespace BlazorTelemetry.Tests;

public sealed class Blazor2faAuthenticationTicketStoreTests
{
    [Fact]
    public void AuthenticationTicketsAreBoundedWhenTokensAreNotConsumed()
    {
        var store = new Blazor2faAuthenticationTicketStore(TimeProvider.System);
        var firstToken = store.Issue([new Claim(ClaimTypes.Email, "first@example.com")]);

        for (var index = 0; index < 1_024; index++)
        {
            store.Issue([new Claim(ClaimTypes.Email, $"user-{index}@example.com")]);
        }

        Assert.False(store.TryConsume(firstToken.ToString(), out _));
    }
}
