using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorTelemetry.Host.Authentication;

public sealed class AuthenticateController(
    ILogger<AuthenticateController> logger,
    UserAuthenticationService authentication,
    BlazorAuthConfiguration settings,
    TimeProvider timeProvider) : Controller
{
    [AllowAnonymous]
    [HttpPost("/b2fa/authenticate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Authenticate([FromForm] string? identifier, [FromForm] string? code,
        [FromForm] string? setupSecret, [FromForm] string? returnUrl)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var _target = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : $"{Request.PathBase}/";
        var _claims = await authentication.Authenticate(identifier ?? "", code ?? "", setupSecret,
            HttpContext.RequestAborted, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (_claims is null)
        {
            return LocalRedirect($"{Request.PathBase}/login?error=true&returnUrl={Uri.EscapeDataString(_target)}");
        }

        var _identity = new ClaimsIdentity(_claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(_identity),
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = true,
                ExpiresUtc = timeProvider.GetUtcNow().AddDays(settings.CookieDurationInDays)
            });
        logger.LogInformation("User {Name} authenticated", _identity.Name);
        return LocalRedirect(_target);
    }

    [HttpPost("/b2fa/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TryLogout()
    {
        Response.Headers.CacheControl = "no-store";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        logger.LogInformation("User {Name} logged out", User.Identity?.Name);
        return LocalRedirect($"{Request.PathBase}/");
    }
}
