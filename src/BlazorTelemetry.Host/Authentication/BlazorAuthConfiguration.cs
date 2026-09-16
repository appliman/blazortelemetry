namespace Blazor2fa;

public class BlazorAuthConfiguration
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string KeyName { get; set; } = string.Empty;
    public List<string> AllowedUserLogins { get; set; } = [];
    public int CookieDurationInDays { get; set; } = 15;
    public string ClaimRole { get; set; } = "Admin";
    public Func<string, string, IServiceProvider, CancellationToken, Task<IReadOnlyCollection<System.Security.Claims.Claim>?>>? ClaimsFactory { get; set; }
}
