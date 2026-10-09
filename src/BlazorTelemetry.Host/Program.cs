using Microsoft.AspNetCore.Authentication;
using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Administration;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Host.Components;
using BlazorTelemetry.Host.Configuration;
using BlazorTelemetry.Host.Contracts.Models.Maintenance;
using BlazorTelemetry.Host.DataProtection;
using BlazorTelemetry.Host.Mcp;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<UserAttemptLimiter>();
builder.Services.AddScoped<UserAuthenticationService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, UserRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, UserSessionAuthorizationHandler>();

var twoFactorConfiguration = builder.Configuration
	.GetSection("Blazor2fa")
	.Get<BlazorAuthConfiguration>()
	?? new BlazorAuthConfiguration();
twoFactorConfiguration.Issuer = string.IsNullOrWhiteSpace(twoFactorConfiguration.Issuer)
	? "BlazorTelemetry"
	: twoFactorConfiguration.Issuer.Trim();
twoFactorConfiguration.ApplicationName = string.IsNullOrWhiteSpace(twoFactorConfiguration.ApplicationName)
	? $"{builder.Environment.EnvironmentName} Administrator"
	: twoFactorConfiguration.ApplicationName.Trim();
builder.AddBlazor2fa(twoFactorConfiguration);
builder.Services.AddControllersWithViews();

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
        options.Events.OnValidatePrincipal = async context =>
        {
            var authentication = context.HttpContext.RequestServices.GetRequiredService<UserAuthenticationService>();
            if (context.Principal is null || !await authentication.IsCurrent(context.Principal, context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
	});

builder.Services.AddAuthorizationBuilder()
	.AddPolicy("BlazorTelemetryReader", policy => policy.RequireRole("Reader", "Administrator").AddRequirements(new UserSessionRequirement()))
	.AddPolicy("BlazorTelemetryAdministrator", policy => policy.RequireRole("Administrator").AddRequirements(new UserSessionRequirement()));

var baseTelemetryOptions = builder.Configuration
	.GetSection(BlazorTelemetryOptions.SECTION_NAME)
	.Get<BlazorTelemetryOptions>() ?? new BlazorTelemetryOptions();

var optionsOverrideStore = new TelemetryOptionsOverrideStore(baseTelemetryOptions.ConnectionString);
builder.Services.AddSingleton(optionsOverrideStore);
builder.Services.AddBlazorTelemetry(builder.Configuration, optionsOverrideStore.Apply);
builder.Services.AddMcpServer()
	.WithHttpTransport(options => options.Stateless = true)
	.WithToolsFromAssembly(typeof(TelemetryMcpTools).Assembly);
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddSingleton<DatabaseBackupWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<DatabaseBackupWorker>());

builder.Services.AddSingleton<DataProtectionKeyRepository>();
builder.Services.AddDataProtection().SetApplicationName("BlazorTelemetry.Host");
builder.Services.AddOptions<KeyManagementOptions>()
	.Configure<DataProtectionKeyRepository>((options, repository) => options.XmlRepository = repository);

var app = builder.Build();
var initialization = await app.Services.GetRequiredService<IMediator>()
	.Send(new InitializeTelemetryDatabaseRequest(), app.Lifetime.ApplicationStopping);
if (initialization.HasError)
{
	throw new InvalidOperationException(initialization.BrokenRules[0].Message);
}
app.Logger.LogInformation(
	"2FA authenticator entry configured as {Issuer}:{ApplicationName}",
	twoFactorConfiguration.Issuer,
	twoFactorConfiguration.ApplicationName);
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	app.UseHsts();
}

app.UseWhen(
	context => !context.Request.Path.StartsWithSegments("/v1") && !context.Request.Path.StartsWithSegments("/blazor-telemetry") && !context.Request.Path.StartsWithSegments("/mcp"),
	branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseBlazorTelemetry();
app.UseHttpsRedirection();
app.UseWhen(context => context.Request.Path.StartsWithSegments("/mcp"), branch => branch.UseMiddleware<McpKeyMiddleware>());
app.UseAuthentication();
app.UseAuthorization();
app.UseBlazor2fa();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapBlazorTelemetry();
app.MapMcp("/mcp");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
