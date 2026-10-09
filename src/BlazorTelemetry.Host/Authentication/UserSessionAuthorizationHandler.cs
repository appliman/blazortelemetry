using Microsoft.AspNetCore.Authorization;

namespace BlazorTelemetry.Host.Authentication;

public sealed class UserSessionAuthorizationHandler(UserAuthenticationService authentication) : AuthorizationHandler<UserSessionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, UserSessionRequirement requirement)
    {
        if (await authentication.IsCurrent(context.User, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
