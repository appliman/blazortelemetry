using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlazorTelemetry.Demo;

public sealed class DemoAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState STATE = new(new ClaimsPrincipal(new ClaimsIdentity([
        new Claim(ClaimTypes.Name, "Demo user"),
        new Claim(ClaimTypes.Role, "Administrator")
    ], "Demo")));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(STATE);
}
