using System.Net;
using System.Text;
using System.Text.Json;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Sqlite;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlazorTelemetry.Tests;

public sealed class AuthenticationHttpTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"telemetry-auth-http-{Guid.NewGuid():N}.db");
    private readonly TestTimeProvider _clock = new();
    private readonly BlazorAuthConfiguration _settings = new() { AllowedUserLogins = ["admin"], SecretKey = "test-only-administrator-secret" };
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var _builder = WebApplication.CreateBuilder();
        _builder.Configuration.Sources.Clear();
        _builder.Configuration.AddInMemoryCollection();
        _builder.Logging.ClearProviders();
        _builder.WebHost.UseUrls("http://127.0.0.1:0");
        _builder.Services.AddDbContextFactory<TelemetryDbContext>(options => options.UseSqlite($"Data Source={_path};Pooling=False"));
        _builder.Services.AddSingleton<TimeProvider>(_clock);
        _builder.Services.AddSingleton(_settings);
        _builder.Services.AddSingleton<UserAttemptLimiter>();
        _builder.Services.AddScoped<UserAuthenticationService>();
        _builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AuthenticateController).Assembly);
        _builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        _builder.Services.AddAuthorization();
        _app = _builder.Build();
        await using (var _scope = _app.Services.CreateAsyncScope())
        {
            var _context = _scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
            await _context.Database.MigrateAsync();
        }
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.MapControllers();
        _app.MapGet("/test/form", (Microsoft.AspNetCore.Http.HttpContext context, IAntiforgery antiforgery) =>
        {
            var _tokens = antiforgery.GetAndStoreTokens(context);
            return new { field = _tokens.FormFieldName, token = _tokens.RequestToken };
        });
        _app.MapGet("/test/session", (Microsoft.AspNetCore.Http.HttpContext context) => context.User.Identity?.Name ?? "anonymous");
        await _app.StartAsync();
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    [Theory]
    [InlineData("/b2fa/authenticate")]
    [InlineData("/b2fa/logout")]
    public async Task AuthenticationMutationsRejectGetAndMissingAntiforgery(string route)
    {
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync(route, new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task CredentialsStayInPostBodyAndAuthenticationRejectsReplay()
    {
        var _code = UserTotp.Code(Encoding.UTF8.GetBytes(_settings.SecretKey), _clock.GetUtcNow().ToUnixTimeSeconds() / 30);
        var _form = await Form();
        _form["identifier"] = "admin";
        _form["code"] = _code;
        _form["returnUrl"] = "https://untrusted.example/";
        var _response = await _client.PostAsync("/b2fa/authenticate", new FormUrlEncodedContent(_form));
        Assert.Equal(HttpStatusCode.Redirect, _response.StatusCode);
        Assert.Equal("/", _response.Headers.Location?.OriginalString);
        Assert.Contains(_response.Headers.GetValues("Set-Cookie"), value => value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("admin", await _client.GetStringAsync("/test/session"));
        var _replayForm = await Form();
        _replayForm["identifier"] = "admin";
        _replayForm["code"] = _code;
        var _replay = await _client.PostAsync("/b2fa/authenticate", new FormUrlEncodedContent(_replayForm));
        Assert.Equal(HttpStatusCode.Redirect, _replay.StatusCode);
        Assert.StartsWith("/login?error=true", _replay.Headers.Location?.OriginalString);
        Assert.False(_replay.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/b2fa/authenticate/{Guid.NewGuid()}" )).StatusCode);
    }

    [Fact]
    public async Task LogoutRequiresAntiforgeryAndClearsAuthenticatedSession()
    {
        var _form = await Form();
        _form["identifier"] = "admin";
        _form["code"] = UserTotp.Code(Encoding.UTF8.GetBytes(_settings.SecretKey), _clock.GetUtcNow().ToUnixTimeSeconds() / 30);
        Assert.Equal(HttpStatusCode.Redirect, (await _client.PostAsync("/b2fa/authenticate", new FormUrlEncodedContent(_form))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/b2fa/logout", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal("admin", await _client.GetStringAsync("/test/session"));
        Assert.Equal(HttpStatusCode.Redirect, (await _client.PostAsync("/b2fa/logout", new FormUrlEncodedContent(await Form()))).StatusCode);
        Assert.Equal("anonymous", await _client.GetStringAsync("/test/session"));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        File.Delete(_path);
    }

    private async Task<Dictionary<string, string>> Form()
    {
        using var _document = JsonDocument.Parse(await _client.GetStringAsync("/test/form"));
        return new() { [_document.RootElement.GetProperty("field").GetString()!] = _document.RootElement.GetProperty("token").GetString()! };
    }
}
