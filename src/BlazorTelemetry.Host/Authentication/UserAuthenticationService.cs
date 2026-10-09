using System.Security.Claims;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Authentication;

public sealed class UserAuthenticationService(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    IDataProtectionProvider protection,
    BlazorAuthConfiguration settings,
    UserAttemptLimiter limiter,
    TimeProvider timeProvider,
    ILogger<UserAuthenticationService>? logger = null)
{
    public const string USER_ID = "telemetry_user_id";
    public const string SECURITY_VERSION = "telemetry_security_version";
    private readonly IDataProtector _protector = protection.CreateProtector("BlazorTelemetry.Users.Secret.v1");

    public string Protect(Guid _secret) => _protector.Protect(_secret.ToString("D"));

    public bool IsAdministrator(ClaimsPrincipal principal)
    {
        var _identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return principal.Identity?.IsAuthenticated == true && principal.IsInRole("Administrator")
            && settings.AllowedUserLogins.Contains(_identifier ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsCurrent(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (IsAdministrator(principal))
        {
            return true;
        }
        if (principal.Identity?.IsAuthenticated != true || !long.TryParse(principal.FindFirstValue(USER_ID), out var _id)
            || !Guid.TryParse(principal.FindFirstValue(SECURITY_VERSION), out var _version))
        {
            return false;
        }
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await _context.Users.AnyAsync(_user => _user.Id == _id && _user.IsActive && _user.ActivatedUtc != null && _user.SecurityVersion == _version, cancellationToken);
    }

    public async Task<string?> SetupUri(string _identifier, string _secret, CancellationToken cancellationToken)
    {
        if (!limiter.Allow(_identifier) || !Guid.TryParse(_secret, out var _guid))
        {
            return null;
        }
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var _user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(_user => _user.Identifier == _identifier.Trim(), cancellationToken);
        if (_user is null || !_user.IsActive || _user.ActivatedUtc != null)
        {
            logger?.LogWarning("Authenticator setup rejected because the account is unavailable or already activated.");
            return null;
        }
        if (!SecretMatches(_user, _guid))
        {
            logger?.LogWarning("Authenticator setup rejected because the provided secret did not match.");
            return null;
        }
        var _issuer = Uri.EscapeDataString(settings.Issuer);
        var _label = Uri.EscapeDataString($"{settings.Issuer}:{_user.Identifier}");
        return $"otpauth://totp/{_label}?secret={UserTotp.Base32(UserTotp.Key(_guid, _user.Identifier))}&issuer={_issuer}&algorithm=SHA1&digits=6&period=30";
    }

    public async Task<IReadOnlyCollection<Claim>?> Authenticate(string _identifier, string code, string? setupSecret, CancellationToken cancellationToken)
    {
        _identifier = _identifier.Trim();
        if (!limiter.Allow(_identifier))
        {
            return null;
        }
        var _admin = settings.AllowedUserLogins.Find(value => string.Equals(value, _identifier, StringComparison.OrdinalIgnoreCase));
        if (_admin is not null)
        {
            return !string.IsNullOrWhiteSpace(settings.SecretKey) && new TwoFactorAuthenticator().ValidateTwoFactorPIN(settings.SecretKey, code.Trim())
                ? [new Claim(ClaimTypes.NameIdentifier, _admin), new Claim(ClaimTypes.Name, _admin), new Claim(ClaimTypes.Role, "Administrator")]
                : null;
        }
        await using var _context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var _user = await _context.Users.AsNoTracking().SingleOrDefaultAsync(_user => _user.Identifier == _identifier, cancellationToken);
        if (_user is null || !_user.IsActive)
        {
            return null;
        }
        var _activating = _user.ActivatedUtc is null;
        if (_activating && (!Guid.TryParse(setupSecret, out var _provided) || !SecretMatches(_user, _provided)))
        {
            return null;
        }
        Guid _secret;
        try
        {
            _secret = Guid.Parse(_protector.Unprotect(_user.ProtectedSecret));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        var _now = timeProvider.GetUtcNow();
        var _interval = UserTotp.Match(UserTotp.Key(_secret, _user.Identifier), code.Trim(), _now);
        if (_interval is null || _interval <= _user.LastAcceptedInterval)
        {
            return null;
        }
        var _activated = _user.ActivatedUtc ?? _now;
        var _changed = await _context.Users.Where(value => value.Id == _user.Id && value.IsActive
            && value.SecurityVersion == _user.SecurityVersion && value.LastAcceptedInterval < _interval.Value)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.LastAcceptedInterval, _interval.Value)
                .SetProperty(value => value.ActivatedUtc, _activated), cancellationToken);
        if (_changed != 1)
        {
            return null;
        }
        return [new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()), new Claim(ClaimTypes.Name, _user.Identifier),
            new Claim(ClaimTypes.Role, "Reader"), new Claim(USER_ID, _user.Id.ToString()), new Claim(SECURITY_VERSION, _user.SecurityVersion.ToString("D"))];
    }

    private bool SecretMatches(TelemetryUser _user, Guid _secret)
    {
        try
        {
            return Guid.TryParse(_protector.Unprotect(_user.ProtectedSecret), out var _stored) && _stored == _secret;
        }
        catch (System.Security.Cryptography.CryptographicException _exception)
        {
            logger?.LogError(_exception, "The user secret could not be unprotected.");
            return false;
        }
    }
}
