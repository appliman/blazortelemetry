using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace BlazorTelemetry.Host.Authentication;

public sealed class UserRevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, UserAuthenticationService authentication)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(15);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
        => authentication.IsCurrent(authenticationState.User, cancellationToken);
}
