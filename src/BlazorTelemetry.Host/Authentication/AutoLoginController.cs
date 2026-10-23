using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorTelemetry.Host.Authentication;

[AllowAnonymous]
public sealed class AutoLoginController(
    AutoLoginCodeStore codes,
    AutoLoginKeyService keys,
    UserAuthenticationService authentication,
    BlazorAuthConfiguration settings,
    TimeProvider timeProvider) : Controller
{
    [HttpPost("/api/auth/auto-login")]
    public async Task<IActionResult> Issue()
    {
        Response.Headers.CacheControl = "no-store";
        var _authorization = Request.Headers.Authorization.ToString();
        if (!_authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized();
        }
        var _supplied = _authorization[7..].Trim();
        if (string.IsNullOrWhiteSpace(_supplied))
        {
            return Unauthorized();
        }

        var _key = await keys.Authenticate(_supplied, HttpContext.RequestAborted);
        if (_key.HasError)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (_key.Data is null)
        {
            return Unauthorized();
        }
        var _ticket = codes.Issue(_key.Data);
        if (_ticket is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return Ok(new
        {
            code = _ticket.Code,
            loginPath = $"{Request.PathBase}/auto-login?code={_ticket.Code}",
            expiresUtc = _ticket.ExpiresUtc
        });
    }

    [HttpGet("/auto-login")]
    public async Task<IActionResult> Redeem([FromQuery] string? code)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!codes.TryConsume(code, out var _key) || _key is null || !await keys.IsCurrent(_key, HttpContext.RequestAborted))
        {
            return LocalRedirect($"{Request.PathBase}/login");
        }

        if (!await authentication.IsCurrent(User, HttpContext.RequestAborted))
        {
            var _identity = new ClaimsIdentity(AutoLoginKeyService.CreateClaims(_key), CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(_identity),
                new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = true,
                    ExpiresUtc = timeProvider.GetUtcNow().AddDays(settings.CookieDurationInDays)
                });
        }
        return LocalRedirect($"{Request.PathBase}/");
    }
}
