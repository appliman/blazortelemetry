using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Host.Components;
using Blazor2fa;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

var twoFactorConfiguration = builder.Configuration
    .GetSection("Blazor2fa")
    .Get<BlazorAuthConfiguration>()
    ?? new BlazorAuthConfiguration();
twoFactorConfiguration.ClaimsFactory = (email, code, _, _) =>
{
    var normalizedEmail = email.Trim();
    var isAllowed = twoFactorConfiguration.AllowedUserLogins.Contains(
        normalizedEmail,
        StringComparer.OrdinalIgnoreCase);
    var isCodeValid = !string.IsNullOrWhiteSpace(twoFactorConfiguration.SecretKey)
        && new TwoFactorAuthenticator().ValidateTwoFactorPIN(
            twoFactorConfiguration.SecretKey,
            code.Trim());
    if (!isAllowed || !isCodeValid)
    {
        return Task.FromResult<IReadOnlyCollection<Claim>?>(null);
    }

    IReadOnlyCollection<Claim> claims =
    [
        new Claim(ClaimTypes.NameIdentifier, normalizedEmail),
        new Claim(ClaimTypes.Name, normalizedEmail),
        new Claim(ClaimTypes.Email, normalizedEmail),
        new Claim(ClaimTypes.Role, "Administrator")
    ];
    return Task.FromResult<IReadOnlyCollection<Claim>?>(claims);
};
builder.AddBlazor2fa(twoFactorConfiguration);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "BlazorTelemetry.Authentication";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(twoFactorConfiguration.CookieDurationInDays);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BlazorTelemetryReader", policy => policy.RequireRole("Reader", "Administrator"))
    .AddPolicy("BlazorTelemetryAdministrator", policy => policy.RequireRole("Administrator"));
builder.Services.AddBlazorTelemetry(builder.Configuration);
builder.Services.AddHealthChecks();

var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    Directory.CreateDirectory(keysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("BlazorTelemetry.Host");
}

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/v1") && !context.Request.Path.StartsWithSegments("/blazor-telemetry"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseBlazorTelemetry();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseBlazor2fa();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapBlazorTelemetry();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(BlazorTelemetry.TelemetryDashboard).Assembly);
app.Run();

public partial class Program;
