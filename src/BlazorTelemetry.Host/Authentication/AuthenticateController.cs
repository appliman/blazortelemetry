using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Blazor2fa;

public class AuthenticateController(
    ILogger<AuthenticateController> logger,
    Blazor2faAuthenticationTicketStore ticketStore,
    BlazorAuthConfiguration settings
    )
    : Controller
{
    [AllowAnonymous]
    [HttpGet]
    [Route("/b2fa/authenticate/{token:guid}")]
    public async Task<IActionResult> Authenticate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Token is empty");
            return Redirect("/");
        }

        if (!ticketStore.TryConsume(token, out var claims))
        {
            logger.LogWarning("Authentication token is invalid, expired or already consumed");
            return Redirect("/");
        }

        var claimsIdentity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var userPrincipal = new ClaimsPrincipal(claimsIdentity);

        var authProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(settings.CookieDurationInDays)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            userPrincipal,
            authProperties);

        var returnUrl = Request.Query
            .FirstOrDefault(parameter => string.Equals(
                parameter.Key,
                "ReturnUrl",
                StringComparison.OrdinalIgnoreCase))
            .Value
            .ToString();
        if (!string.IsNullOrWhiteSpace(returnUrl)
            && Url.IsLocalUrl(returnUrl))
        {
            logger.LogInformation(
                "User {Name} authenticated and redirected to {ReturnUrl}",
                claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value,
                returnUrl);
            return Redirect(returnUrl);
        }

        return Redirect("/");
    }

    [HttpGet]
    [Route("/b2fa/logout")]
    public async Task<IActionResult> TryLogout()
    {
        if (User.Identity == null
            || !User.Identity.IsAuthenticated)
        {
            return Redirect("/");
        }
        await HttpContext.SignOutAsync();

        logger.LogInformation("User {Name} logged out", User.Identity.Name);

        return Redirect("/");
    }
}
