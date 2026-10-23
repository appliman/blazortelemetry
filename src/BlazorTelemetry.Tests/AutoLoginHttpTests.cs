using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using ChannelMediator.InMemory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorTelemetry.Tests;

public sealed class AutoLoginHttpTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"telemetry-auto-login-http-{Guid.NewGuid():N}.db");
    private readonly TestTimeProvider _clock = new();
    private readonly BlazorTelemetryOptions _options = new();
    private readonly BlazorAuthConfiguration _settings = new() { AllowedUserLogins = ["test-admin"], CookieDurationInDays = 15 };
    private readonly (string PlainText, string Hash, string Prefix) _generated = IngestionKeyGenerator.Generate();
    private IDbContextFactory<TelemetryDbContext> _factory = null!;
    private WebApplication _app = null!;
    private HttpClientHandler _handler = null!;
    private HttpClient _client = null!;
    private int _keyId;
    private bool _failSignIn;

    public async Task InitializeAsync()
    {
        _factory = new TestDbContextFactory(new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False").Options);
        await using (var _context = await _factory.CreateDbContextAsync())
        {
            await _context.Database.EnsureCreatedAsync();
            var _key = new IngestionApplication { Name = "HTTP test key", KeyHash = _generated.Hash, KeyPrefix = _generated.Prefix };
            _context.IngestionApplications.Add(_key);
            await _context.SaveChangesAsync();
            _keyId = _key.Id;
        }

        var _builder = WebApplication.CreateBuilder();
        _builder.Configuration.Sources.Clear();
        _builder.Configuration.AddInMemoryCollection();
        _builder.Logging.ClearProviders();
        _builder.WebHost.UseUrls("http://127.0.0.1:0");
        _builder.Services.AddSingleton<IDbContextFactory<TelemetryDbContext>>(_factory);
        _builder.Services.AddSingleton<TimeProvider>(_clock);
        _builder.Services.AddSingleton(_options);
        _builder.Services.AddSingleton(_settings);
        _builder.Services.AddSingleton<UserAttemptLimiter>();
        _builder.Services.AddSingleton<AutoLoginCodeStore>();
        _builder.Services.AddScoped<AutoLoginKeyService>();
        _builder.Services.AddScoped<UserAuthenticationService>();
        _builder.Services.AddScoped<IAuthorizationHandler, UserSessionAuthorizationHandler>();
        _builder.Services.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(BlazorTelemetryServiceCollectionExtensions).Assembly);
        _builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AutoLoginController).Assembly);
        _builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(_cookie =>
        {
            _cookie.LoginPath = "/login";
            _cookie.AccessDeniedPath = "/login";
            _cookie.SlidingExpiration = true;
            _cookie.Events.OnSigningIn = _ => _failSignIn ? Task.FromException(new InvalidOperationException("Test cookie failure.")) : Task.CompletedTask;
            _cookie.Events.OnValidatePrincipal = async _context =>
            {
                var _authentication = _context.HttpContext.RequestServices.GetRequiredService<UserAuthenticationService>();
                if (_context.Principal is null || !await _authentication.IsCurrent(_context.Principal, _context.HttpContext.RequestAborted))
                {
                    _context.RejectPrincipal();
                    await _context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
        });
        _builder.Services.AddAuthorizationBuilder()
            .AddPolicy("BlazorTelemetryReader", policy => policy.RequireRole("Reader", "Administrator").AddRequirements(new UserSessionRequirement()))
            .AddPolicy("BlazorTelemetryAdministrator", policy => policy.RequireRole("Administrator").AddRequirements(new UserSessionRequirement()));
        _app = _builder.Build();
        _app.UsePathBase("/telemetry");
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapGet("/test/reader", () => Results.Ok()).RequireAuthorization("BlazorTelemetryReader");
        _app.MapGet("/test/admin", () => Results.Ok()).RequireAuthorization("BlazorTelemetryAdministrator");
        _app.MapGet("/test/sign-in", async (HttpContext context) =>
        {
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-admin"), new Claim(ClaimTypes.Role, "Administrator")], CookieAuthenticationDefaults.AuthenticationScheme)));
        });
        await _app.StartAsync();
        _handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        _client = new HttpClient(_handler) { BaseAddress = new Uri(_app.Urls.Single() + "/telemetry/") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        File.Delete(_path);
    }

    [Fact]
    public async Task IssuanceAndRedemptionCreatePersistentReaderCookieAndRemoveCodeFromRedirect()
    {
        var _issued = await Issue(_generated.PlainText);
        Assert.StartsWith("/telemetry/auto-login?code=", _issued.LoginPath);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(5), _issued.ExpiresUtc);
        using var _response = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal(HttpStatusCode.Redirect, _response.StatusCode);
        Assert.Equal("/telemetry/", _response.Headers.Location?.OriginalString);
        Assert.True(_response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(_response.Headers.GetValues("Referrer-Policy")));

        var _ticket = ReadTicket();
        Assert.True(_ticket.Principal.IsInRole("Reader"));
        Assert.False(_ticket.Principal.IsInRole("Administrator"));
        Assert.True(_ticket.Properties.IsPersistent);
        Assert.True(_ticket.Properties.AllowRefresh);
        // Cookie timestamps are serialized to whole seconds.
        Assert.InRange((_ticket.Properties.ExpiresUtc!.Value - _clock.GetUtcNow().AddDays(15)).TotalSeconds, -1, 0);
        using var _reader = await _client.GetAsync("test/reader");
        Assert.Equal(HttpStatusCode.OK, _reader.StatusCode);
        using var _admin = await _client.GetAsync("test/admin");
        Assert.NotEqual(HttpStatusCode.OK, _admin.StatusCode);

        using var _replay = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal("/telemetry/login", _replay.Headers.Location?.OriginalString);
        Assert.False(_replay.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic invalid")]
    [InlineData("Bearer unknown")]
    [InlineData("Bearer ")]
    public async Task MissingAndInvalidKeysAreUnauthorizedEvenWhenAnonymousIngestionIsEnabled(string? authorization)
    {
        using var _request = new HttpRequestMessage(HttpMethod.Post, "api/auth/auto-login");
        if (authorization is not null)
        {
            _request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        using var _response = await _client.SendAsync(_request);
        Assert.Equal(HttpStatusCode.Unauthorized, _response.StatusCode);
        Assert.True(_response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task McpAndConfiguredKeysCanIssueCodes()
    {
        await ChangeKey(key => key.IsMcpReadKey = true);
        await Issue(_generated.PlainText);
        _options.IngestionKeys["Configured application"] = "short-key";
        var _issued = await Issue("short-key");
        using var _response = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal("/telemetry/", _response.Headers.Location?.OriginalString);
        Assert.Equal("configuration", ReadTicket().Principal.FindFirstValue(AutoLoginKeyService.KEY_SOURCE));
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("rotate")]
    [InlineData("expire")]
    public async Task KeyChangesInvalidatePendingCodesAndExistingCookies(string change)
    {
        var _login = await Issue(_generated.PlainText);
        using var _signedIn = await _client.GetAsync(_login.LoginPath);
        var _pending = await Issue(_generated.PlainText);
        await ChangeKey(key =>
        {
            if (change == "revoke")
            {
                key.IsActive = false;
                key.RevokedUtc = _clock.GetUtcNow();
            }
            else if (change == "rotate")
            {
                key.KeyHash = IngestionKeyGenerator.Generate().Hash;
            }
            else
            {
                key.ExpiresUtc = _clock.GetUtcNow();
            }
        });

        using var _redemption = await _client.GetAsync(_pending.LoginPath);
        Assert.Equal("/telemetry/login", _redemption.Headers.Location?.OriginalString);
        Assert.False(_app.Services.GetRequiredService<AutoLoginCodeStore>().TryConsume(_pending.Code, out _));
        using var _reader = await _client.GetAsync("test/reader");
        Assert.NotEqual(HttpStatusCode.OK, _reader.StatusCode);
        using var _issue = await Post(_generated.PlainText);
        Assert.Equal(HttpStatusCode.Unauthorized, _issue.StatusCode);
    }

    [Fact]
    public async Task ExistingAdministratorSessionIsPreservedAndCodeConsumed()
    {
        using var _adminLogin = await _client.GetAsync("test/sign-in");
        var _cookie = _handler.CookieContainer.GetCookieHeader(_client.BaseAddress!);
        var _issued = await Issue(_generated.PlainText);
        using var _response = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal("/telemetry/", _response.Headers.Location?.OriginalString);
        Assert.False(_response.Headers.Contains("Set-Cookie"));
        Assert.Equal(_cookie, _handler.CookieContainer.GetCookieHeader(_client.BaseAddress!));
        Assert.False(_app.Services.GetRequiredService<AutoLoginCodeStore>().TryConsume(_issued.Code, out _));
        using var _admin = await _client.GetAsync("test/admin");
        Assert.Equal(HttpStatusCode.OK, _admin.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?code=unknown")]
    [InlineData("?code=%3Cscript%3Ealert(1)%3C%2Fscript%3E")]
    public async Task MissingAndMalformedCodesRedirectWithoutCreatingCookie(string query)
    {
        using var _response = await _client.GetAsync("auto-login" + query);
        Assert.Equal("/telemetry/login", _response.Headers.Location?.OriginalString);
        Assert.False(_response.Headers.Contains("Set-Cookie"));
        Assert.True(_response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(_response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task CodeExpiresAtFiveMinutes()
    {
        var _issued = await Issue(_generated.PlainText);
        _clock.Advance(TimeSpan.FromMinutes(5));
        using var _response = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal("/telemetry/login", _response.Headers.Location?.OriginalString);
        Assert.False(_response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SaturatedStoreReturnsServiceUnavailable()
    {
        var _store = _app.Services.GetRequiredService<AutoLoginCodeStore>();
        for (var _index = 0; _index < 1024; _index++)
        {
            Assert.NotNull(_store.Issue(new AutoLoginKey("database", _keyId.ToString(), _generated.Hash, "Test")));
        }
        using var _response = await Post(_generated.PlainText);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, _response.StatusCode);
        Assert.True(_response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task StorageFailureReturnsServiceUnavailable()
    {
        await using var _context = await _factory.CreateDbContextAsync();
        await _context.Database.EnsureDeletedAsync();
        using var _response = await Post(_generated.PlainText);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, _response.StatusCode);
        Assert.True(_response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task CookieFailureStillConsumesCode()
    {
        var _issued = await Issue(_generated.PlainText);
        _failSignIn = true;
        using var _response = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal(HttpStatusCode.InternalServerError, _response.StatusCode);
        Assert.False(_app.Services.GetRequiredService<AutoLoginCodeStore>().TryConsume(_issued.Code, out _));
        _failSignIn = false;
        using var _replay = await _client.GetAsync(_issued.LoginPath);
        Assert.Equal("/telemetry/login", _replay.Headers.Location?.OriginalString);
    }

    private async Task<(string Code, string LoginPath, DateTimeOffset ExpiresUtc)> Issue(string secret)
    {
        using var _response = await Post(secret);
        Assert.Equal(HttpStatusCode.OK, _response.StatusCode);
        Assert.True(_response.Headers.CacheControl?.NoStore);
        using var _json = JsonDocument.Parse(await _response.Content.ReadAsStringAsync());
        return (_json.RootElement.GetProperty("code").GetString()!, _json.RootElement.GetProperty("loginPath").GetString()!,
            _json.RootElement.GetProperty("expiresUtc").GetDateTimeOffset());
    }

    private async Task<HttpResponseMessage> Post(string secret)
    {
        using var _request = new HttpRequestMessage(HttpMethod.Post, "api/auth/auto-login");
        _request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return await _client.SendAsync(_request);
    }

    private async Task ChangeKey(Action<IngestionApplication> change)
    {
        await using var _context = await _factory.CreateDbContextAsync();
        var _key = await _context.IngestionApplications.SingleAsync(key => key.Id == _keyId);
        change(_key);
        await _context.SaveChangesAsync();
    }

    private AuthenticationTicket ReadTicket()
    {
        var _cookieOptions = _app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var _cookie = _handler.CookieContainer.GetCookies(_client.BaseAddress!)[_cookieOptions.Cookie.Name!];
        return Assert.IsType<AuthenticationTicket>(_cookieOptions.TicketDataFormat.Unprotect(Assert.IsType<Cookie>(_cookie).Value));
    }
}
