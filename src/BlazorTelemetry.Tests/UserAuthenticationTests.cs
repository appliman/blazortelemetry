using System.Security.Claims;
using System.Text;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Host.Contracts.Models.Users;
using BlazorTelemetry.Host.Handlers.Users;
using BlazorTelemetry.Host.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorTelemetry.Tests;

public sealed class UserAuthenticationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"telemetry-users-{Guid.NewGuid():N}.db");
    private readonly TestTimeProvider _clock = new();
    private readonly BlazorAuthConfiguration _settings = new() { AllowedUserLogins = ["admin"], SecretKey = "configured-admin-secret", Issuer = "Telemetry" };
    private readonly UserAttemptLimiter _limiter;
    private readonly TestContextFactory _factory;
    private readonly UserAuthenticationService _authentication;
    private readonly SaveUserRequestHandler _save;
    private readonly ClaimsPrincipal _admin = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "admin"), new(ClaimTypes.Role, "Administrator")], "test"));

    public UserAuthenticationTests()
    {
        _factory = new(new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_path};Pooling=False").UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        using var _context = _factory.CreateDbContext();
        _context.Database.Migrate();
        _limiter = new(_clock);
        _authentication = new(_factory, new EphemeralDataProtectionProvider(), _settings, _limiter, _clock);
        _save = new(_factory, _authentication, new TelemetryUserValidator(_settings), NullLogger<SaveUserRequestHandler>.Instance);
    }

    [Fact]
    public async Task CreationStoresOneIdentifierAndProtectsSecret()
    {
        var _user = new TelemetryUser { Identifier = "  Alice  " };
        var _result = await _save.Handle(new(_user, false, _user.SecurityVersion, _admin), default);
        Assert.False(_result.HasError);
        Assert.True(Guid.TryParse(_result.Data, out _));
        using var _context = _factory.CreateDbContext();
        var _stored = await _context.Users.SingleAsync();
        Assert.Equal("Alice", _stored.Identifier);
        Assert.DoesNotContain(_result.Data!, _stored.ProtectedSecret);
        Assert.Null(_stored.ActivatedUtc);
        var _list = await new GetUsersRequestHandler(_factory, _authentication, NullLogger<GetUsersRequestHandler>.Instance).Handle(new(_admin), default);
        Assert.Empty(Assert.Single(_list.Data!).ProtectedSecret);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ADMIN ")]
    public async Task InvalidOrReservedIdentifiersAreRejected(string identifier)
    {
        var _user = new TelemetryUser { Identifier = identifier };
        Assert.True((await _save.Handle(new(_user, false, _user.SecurityVersion, _admin), default)).HasError);
    }

    [Fact]
    public async Task CaseInsensitiveDuplicatesAreRejectedByHandlerAndDatabase()
    {
        await Create("Alice");
        var _user = new TelemetryUser { Identifier = "alice" };
        Assert.True((await _save.Handle(new(_user, false, _user.SecurityVersion, _admin), default)).HasError);
        using var _context = _factory.CreateDbContext();
        _context.Users.Add(_user);
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task OnlyConfiguredAdministratorsCanManageUsers()
    {
        var _forged = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "unknown"), new(ClaimTypes.Role, "Administrator")], "test"));
        Assert.True((await _save.Handle(new(new(), false, Guid.Empty, _forged), default)).HasError);
        var _list = new GetUsersRequestHandler(_factory, _authentication, NullLogger<GetUsersRequestHandler>.Instance);
        Assert.True((await _list.Handle(new(_forged), default)).HasError);
        Assert.True((await _list.Handle(new(new ClaimsPrincipal()), default)).HasError);
    }

    [Fact]
    public async Task SetupRequiresMatchingSecretAndCodeAndCannotBeShownAfterActivation()
    {
        var (_user, secret) = await Create("Alice");
        Assert.Null(await _authentication.SetupUri("alice", Guid.NewGuid().ToString(), default));
        Assert.Null(await _authentication.Authenticate("Alice", CurrentCode(_user, secret), null, default));
        var _uri = await _authentication.SetupUri("alice", secret, default);
        Assert.Contains("secret=" + UserTotp.Base32(UserTotp.Key(Guid.Parse(secret), _user.Identifier)), _uri);
        Assert.Null(await _authentication.Authenticate("Alice", "wrong", secret, default));
        var _claims = await _authentication.Authenticate("alice", CurrentCode(_user, secret), secret, default);
        Assert.NotNull(_claims);
        Assert.Contains(_claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Reader");
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await _authentication.SetupUri("Alice", secret, default));
        Assert.NotNull(await _authentication.Authenticate("ALICE", CurrentCode(_user, secret), null, default));
    }

    [Fact]
    public async Task ConcurrentCodeReplaySucceedsOnlyOnce()
    {
        var (_user, secret) = await Create("Alice");
        var _code = CurrentCode(_user, secret);
        var _results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _authentication.Authenticate("Alice", _code, secret, default)));
        Assert.Single(_results, _result => _result is not null);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("rename")]
    [InlineData("disable")]
    public async Task AccountChangesRevokeSessionsAndOutstandingTickets(string operation)
    {
        var (_user, secret) = await Create("Alice");
        var _claims = (await _authentication.Authenticate("Alice", CurrentCode(_user, secret), secret, default))!;
        var _principal = new ClaimsPrincipal(new ClaimsIdentity(_claims, "test"));
        Assert.True(await _authentication.IsCurrent(_principal, default));
        var _version = _user.SecurityVersion;
        if (operation == "rename")
        {
            _user.Identifier = "Alice2";
        }
        if (operation == "disable")
        {
            _user.IsActive = false;
        }
        var _result = await _save.Handle(new(_user, operation == "reset", _version, _admin), default);
        Assert.False(_result.HasError);
        Assert.False(await _authentication.IsCurrent(_principal, default));
        var _tickets = new Blazor2faAuthenticationTicketStore(_clock);
        var _ticket = _tickets.Issue(_claims);
        var _controller = new AuthenticateController(NullLogger<AuthenticateController>.Instance, _tickets, _authentication, _settings)
        { ControllerContext = new() { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        Assert.Equal("/login", Assert.IsType<RedirectResult>(await _controller.Authenticate(_ticket.ToString())).Url);
        var _authorization = new AuthorizationHandlerContext([new UserSessionRequirement()], _principal, null);
        await new UserSessionAuthorizationHandler(_authentication).HandleAsync(_authorization);
        Assert.False(_authorization.HasSucceeded);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await _authentication.Authenticate("Alice", CurrentCode(new TelemetryUser { Identifier = "Alice" }, secret), null, default));
        if (operation == "disable")
        {
            _user.IsActive = true;
            await _save.Handle(new(_user, false, _user.SecurityVersion, _admin), default);
            Assert.False(await _authentication.IsCurrent(_principal, default));
        }
        else
        {
            Assert.NotNull(_result.Data);
            Assert.NotNull(await _authentication.SetupUri(_user.Identifier, _result.Data!, default));
        }
    }

    [Fact]
    public async Task DisabledUserCannotSetUpAuthenticator()
    {
        var (_user, secret) = await Create("Disabled");
        _user.IsActive = false;
        Assert.False((await _save.Handle(new(_user, false, _user.SecurityVersion, _admin), default)).HasError);
        Assert.Null(await _authentication.SetupUri(_user.Identifier, secret, default));
        Assert.Null(await _authentication.Authenticate(_user.Identifier, CurrentCode(_user, secret), secret, default));
    }

    [Fact]
    public async Task ResetPersistsWithTheProductionNoTrackingConfiguration()
    {
        var (_user, secret) = await Create("Alice");
        await _authentication.Authenticate(_user.Identifier, CurrentCode(_user, secret), secret, default);
        var _oldVersion = _user.SecurityVersion;
        var _result = await _save.Handle(new(_user, true, _oldVersion, _admin), default);
        Assert.False(_result.HasError);
        using var _context = _factory.CreateDbContext();
        var _stored = await _context.Users.SingleAsync();
        Assert.Null(_stored.ActivatedUtc);
        Assert.Equal(-1, _stored.LastAcceptedInterval);
        Assert.NotEqual(_oldVersion, _stored.SecurityVersion);
        Assert.Null(await _authentication.SetupUri(_user.Identifier, secret, default));
        Assert.NotNull(await _authentication.SetupUri(_user.Identifier, _result.Data!, default));
    }

    [Fact]
    public async Task StaleEditDoesNotOverwriteNewSecurityVersion()
    {
        var (_user, _) = await Create("Alice");
        var _version = _user.SecurityVersion;
        await _save.Handle(new(_user, true, _version, _admin), default);
        Assert.True((await _save.Handle(new(_user, false, _version, _admin), default)).HasError);
    }

    [Fact]
    public async Task ConfiguredAdministratorKeepsExistingTotp()
    {
        var _code = new TwoFactorAuthenticator().GetCurrentPIN(_settings.SecretKey);
        var _claims = await _authentication.Authenticate("ADMIN", _code, null, default);
        Assert.Contains(_claims!, claim => claim.Type == ClaimTypes.Role && claim.Value == "Administrator");
    }

    [Fact]
    public void TotpMatchesPublishedRfcVectorAndRejectsExpiredCodes()
    {
        var _key = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal("287082", UserTotp.Code(_key, 1));
        Assert.NotNull(UserTotp.Match(_key, "287082", DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Null(UserTotp.Match(_key, "287082", DateTimeOffset.FromUnixTimeSeconds(150)));
        Assert.Null(UserTotp.Match(_key, "x87082", DateTimeOffset.FromUnixTimeSeconds(59)));
    }

    [Fact]
    public void InteractiveAttemptsAreLimitedAndRecoverAfterWindow()
    {
        for (var _index = 0; _index < 5; _index++)
        {
            Assert.True(_limiter.Allow("Alice"));
        }
        Assert.False(_limiter.Allow(" alice "));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(_limiter.Allow("Alice"));
    }

    [Fact]
    public async Task MigrationUpgradesExistingSchemaWithoutLosingData()
    {
        var _pathValue = Path.Combine(Path.GetTempPath(), $"telemetry-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using var _context = new TelemetryDbContext(new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_pathValue};Pooling=False").Options);
            var _migrator = _context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await _migrator.MigrateAsync("20261002135328_TypedTelemetryStorage");
            _context.Dashboards.Add(new DashboardDefinition { Name = "Existing" });
            await _context.SaveChangesAsync();
            await _context.Database.MigrateAsync();
            Assert.Equal("Existing", (await _context.Dashboards.SingleAsync()).Name);
            Assert.Empty(await _context.Users.ToListAsync());
        }
        finally
        {
            File.Delete(_pathValue);
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
        File.Delete(_path);
    }

    private async Task<(TelemetryUser User, string Secret)> Create(string identifier)
    {
        var _user = new TelemetryUser { Identifier = identifier };
        var _result = await _save.Handle(new(_user, false, _user.SecurityVersion, _admin), default);
        Assert.False(_result.HasError);
        return (_user, _result.Data!);
    }

    private string CurrentCode(TelemetryUser _user, string secret) => UserTotp.Code(UserTotp.Key(Guid.Parse(secret), _user.Identifier), _clock.GetUtcNow().ToUnixTimeSeconds() / 30);
}
