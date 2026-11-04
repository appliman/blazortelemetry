namespace BlazorTelemetry.Host.Authentication;

public class BlazorAuthConfiguration
{
    public string SecretKey { get; set; } = string.Empty;
    public Dictionary<string, string> AdministratorSecrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Issuer { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public List<string> AllowedUserLogins { get; set; } = [];
    public int CookieDurationInDays { get; set; } = 15;
    public string ClaimRole { get; set; } = "Admin";
    public Func<string, string, IServiceProvider, CancellationToken, Task<IReadOnlyCollection<System.Security.Claims.Claim>?>>? ClaimsFactory { get; set; }

    public string? GetAdministratorSecret(string identifier)
    {
        var _entry = AdministratorSecrets.FirstOrDefault(entry => string.Equals(entry.Key.Trim(), identifier.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(_entry.Value))
        {
            return _entry.Value;
        }

        return AllowedUserLogins.Count == 1 && string.Equals(AllowedUserLogins[0].Trim(), identifier.Trim(), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(SecretKey) ? SecretKey : null;
    }

    public void ValidateProductionConfiguration()
    {
        if (CookieDurationInDays is < 1 or > 30)
        {
            throw new InvalidOperationException("The authentication cookie lifetime must be between 1 and 30 days.");
        }
        if (AllowedUserLogins.Count == 0 || AllowedUserLogins.Any(identifier => string.IsNullOrWhiteSpace(identifier)
            || identifier.Length > 256 || identifier != identifier.Trim() || identifier.Equals("demo@demo.com", StringComparison.OrdinalIgnoreCase))
            || AllowedUserLogins.Distinct(StringComparer.OrdinalIgnoreCase).Count() != AllowedUserLogins.Count)
        {
            throw new InvalidOperationException("Configure distinct, non-demo administrator identifiers before starting in production.");
        }
        if (AdministratorSecrets.Keys.Select(identifier => identifier.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != AdministratorSecrets.Count)
        {
            throw new InvalidOperationException("Configure only one secret entry per administrator identifier.");
        }
        foreach (var _identifier in AllowedUserLogins)
        {
            var _secret = GetAdministratorSecret(_identifier);
            if (_secret is null || _secret.Length < 32 || _secret.Any(char.IsWhiteSpace) || _secret.Distinct().Count() < 8)
            {
                throw new InvalidOperationException("Each administrator must have a strong secret of at least 32 characters. Multiple administrators require individual secrets.");
            }
        }
        var _secrets = AllowedUserLogins.Select(identifier => GetAdministratorSecret(identifier)).ToArray();
        if (_secrets.Distinct(StringComparer.Ordinal).Count() != _secrets.Length)
        {
            throw new InvalidOperationException("Administrator secrets must be unique per account.");
        }
    }
}
