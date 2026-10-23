using System.Globalization;
using System.Security.Claims;
using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Sqlite;
using ChannelMediator;
using ChannelMediator.InMemory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTelemetry.Tests;

public sealed class AutoLoginKeyServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"telemetry-auto-login-{Guid.NewGuid():N}.db");
    private readonly TestTimeProvider _clock = new();
    private readonly BlazorTelemetryOptions _options = new();
    private readonly TestContextFactory _factory;
    private ServiceProvider _provider = null!;
    private AutoLoginKeyService _keys = null!;
    private UserAuthenticationService _authentication = null!;

    public AutoLoginKeyServiceTests()
    {
        _clock.Advance(TimeSpan.FromTicks(-(_clock.GetUtcNow().Ticks % TimeSpan.TicksPerMillisecond)));
        _factory = new(new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False").Options);
    }

    public async Task InitializeAsync()
    {
        await using var _context = _factory.CreateDbContext();
        await _context.Database.EnsureCreatedAsync();
        var _services = new ServiceCollection();
        _services.AddLogging();
        _services.AddSingleton<IDbContextFactory<TelemetryDbContext>>(_factory);
        _services.AddSingleton<TimeProvider>(_clock);
        _services.AddSingleton(_options);
        _services.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(BlazorTelemetryServiceCollectionExtensions).Assembly);
        _provider = _services.BuildServiceProvider();
        _keys = new(_provider.GetRequiredService<IMediator>(), _options);
        _authentication = new(_factory, new EphemeralDataProtectionProvider(), new BlazorAuthConfiguration(),
            new UserAttemptLimiter(_clock), _clock, autoLoginKeys: _keys);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveDatabaseKeysAuthenticateWithoutRestrictingTheirPurpose(bool isMcpReadKey)
    {
        var (_application, _secret) = await CreateDatabaseKey(isMcpReadKey);

        var _result = await _keys.Authenticate(_secret, default);

        Assert.False(_result.HasError);
        var _key = Assert.IsType<AutoLoginKey>(_result.Data);
        Assert.Equal("database", _key.Source);
        Assert.Equal(_application.Id.ToString(CultureInfo.InvariantCulture), _key.Id);
        Assert.Equal(_application.KeyHash, _key.Hash);
        Assert.Equal(_application.Name, _key.Name);
        Assert.True(await _keys.IsCurrent(_key, default));
    }

    [Fact]
    public async Task ConfiguredKeyAuthenticatesWithItsConfiguredIdentity()
    {
        _options.IngestionKeys["external-client"] = "configured-secret";

        var _result = await _keys.Authenticate("configured-secret", default);

        Assert.False(_result.HasError);
        var _key = Assert.IsType<AutoLoginKey>(_result.Data);
        Assert.Equal(new AutoLoginKey("configuration", "external-client",
            IngestionKeyGenerator.ComputeHash("configured-secret"), "external-client"), _key);
        Assert.True(await _keys.IsCurrent(_key, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-secret")]
    public async Task UnknownKeyIsRejectedEvenWhenAnonymousIngestionIsAllowed(string secret)
    {
        _options.RequireIngestionKey = false;

        var _result = await _keys.Authenticate(secret, default);

        Assert.False(_result.HasError);
        Assert.Null(_result.Data);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("revoke")]
    [InlineData("expire")]
    [InlineData("rotate")]
    public async Task DatabaseKeyChangesInvalidateOutstandingCodesAndExistingSessions(string operation)
    {
        var (_application, _secret) = await CreateDatabaseKey();
        var _key = Assert.IsType<AutoLoginKey>((await _keys.Authenticate(_secret, default)).Data);
        var _principal = CreatePrincipal(_key);
        using var _codes = new AutoLoginCodeStore(_clock);
        var _code = Assert.IsType<AutoLoginCode>(_codes.Issue(_key));
        Assert.True(await _authentication.IsCurrent(_principal, default));
        var _replacement = IngestionKeyGenerator.Generate();
        await using (var _context = _factory.CreateDbContext())
        {
            var _stored = await _context.IngestionApplications.SingleAsync();
            switch (operation)
            {
                case "disable":
                    _stored.IsActive = false;
                    break;
                case "revoke":
                    _stored.RevokedUtc = _clock.GetUtcNow();
                    break;
                case "expire":
                    _stored.ExpiresUtc = _clock.GetUtcNow();
                    break;
                case "rotate":
                    _stored.KeyHash = _replacement.Hash;
                    _stored.KeyPrefix = _replacement.Prefix;
                    break;
            }
            await _context.SaveChangesAsync();
        }

        Assert.Null((await _keys.Authenticate(_secret, default)).Data);
        Assert.False(await _keys.IsCurrent(_key, default));
        Assert.False(await _authentication.IsCurrent(_principal, default));
        Assert.True(_codes.TryConsume(_code.Code, out var _ticketKey));
        Assert.False(await _keys.IsCurrent(Assert.IsType<AutoLoginKey>(_ticketKey), default));
        if (operation == "rotate")
        {
            var _newKey = Assert.IsType<AutoLoginKey>((await _keys.Authenticate(_replacement.PlainText, default)).Data);
            Assert.Equal(_application.Id.ToString(CultureInfo.InvariantCulture), _newKey.Id);
            Assert.True(await _authentication.IsCurrent(CreatePrincipal(_newKey), default));
        }
    }

    [Fact]
    public async Task DatabaseKeyExpiresAtExactBoundary()
    {
        var (_application, _secret) = await CreateDatabaseKey();
        await using (var _context = _factory.CreateDbContext())
        {
            var _stored = await _context.IngestionApplications.SingleAsync();
            _stored.ExpiresUtc = _clock.GetUtcNow().AddMinutes(1);
            await _context.SaveChangesAsync();
        }
        var _key = Assert.IsType<AutoLoginKey>((await _keys.Authenticate(_secret, default)).Data);

        _clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(1));
        Assert.True(await _keys.IsCurrent(_key, default));
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Null((await _keys.Authenticate(_secret, default)).Data);
        Assert.False(await _keys.IsCurrent(_key, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingOrRotatingConfiguredKeyInvalidatesCodesAndSessions(bool rotate)
    {
        _options.IngestionKeys["client"] = "original-secret";
        var _key = Assert.IsType<AutoLoginKey>((await _keys.Authenticate("original-secret", default)).Data);
        var _principal = CreatePrincipal(_key);
        using var _codes = new AutoLoginCodeStore(_clock);
        var _code = Assert.IsType<AutoLoginCode>(_codes.Issue(_key));
        Assert.True(await _authentication.IsCurrent(_principal, default));

        if (rotate)
        {
            _options.IngestionKeys["client"] = "replacement-secret";
        }
        else
        {
            _options.IngestionKeys.Remove("client");
        }

        Assert.Null((await _keys.Authenticate("original-secret", default)).Data);
        Assert.False(await _keys.IsCurrent(_key, default));
        Assert.False(await _authentication.IsCurrent(_principal, default));
        Assert.True(_codes.TryConsume(_code.Code, out var _ticketKey));
        Assert.False(await _keys.IsCurrent(Assert.IsType<AutoLoginKey>(_ticketKey), default));
        if (rotate)
        {
            var _replacement = Assert.IsType<AutoLoginKey>((await _keys.Authenticate("replacement-secret", default)).Data);
            Assert.True(await _keys.IsCurrent(_replacement, default));
        }
    }

    [Fact]
    public async Task AuthenticationAndRevalidationDoNotChangeUsageCounters()
    {
        var (_application, _secret) = await CreateDatabaseKey();
        var _lastSeen = _clock.GetUtcNow().AddHours(-1);
        await using (var _context = _factory.CreateDbContext())
        {
            var _stored = await _context.IngestionApplications.SingleAsync();
            _stored.LastSeenUtc = _lastSeen;
            _stored.UsageCount = 57;
            await _context.SaveChangesAsync();
        }

        for (var _index = 0; _index < 3; _index++)
        {
            var _key = Assert.IsType<AutoLoginKey>((await _keys.Authenticate(_secret, default)).Data);
            Assert.True(await _keys.IsCurrent(_key, default));
            Assert.True(await _authentication.IsCurrent(CreatePrincipal(_key), default));
        }

        await using var _verification = _factory.CreateDbContext();
        var _saved = await _verification.IngestionApplications.SingleAsync();
        Assert.Equal(57, _saved.UsageCount);
        Assert.Equal(_lastSeen, _saved.LastSeenUtc);
    }

    [Fact]
    public async Task SharedSessionHasReaderAccessAndCannotUseAdministratorPolicy()
    {
        var (_, _secret) = await CreateDatabaseKey();
        var _key = Assert.IsType<AutoLoginKey>((await _keys.Authenticate(_secret, default)).Data);
        var _principal = CreatePrincipal(_key);
        Assert.True(_principal.IsInRole("Reader"));
        Assert.False(_principal.IsInRole("Administrator"));
        Assert.False(_authentication.IsAdministrator(_principal));
        Assert.True(await _authentication.IsCurrent(_principal, default));
        Assert.DoesNotContain(_principal.Claims, claim => claim.Value.Contains(_secret, StringComparison.Ordinal));
        var _services = new ServiceCollection();
        _services.AddLogging();
        _services.AddSingleton(_authentication);
        _services.AddScoped<IAuthorizationHandler, UserSessionAuthorizationHandler>();
        _services.AddAuthorizationBuilder()
            .AddPolicy("Reader", policy => policy.RequireRole("Reader", "Administrator").AddRequirements(new UserSessionRequirement()))
            .AddPolicy("Administrator", policy => policy.RequireRole("Administrator").AddRequirements(new UserSessionRequirement()));
        await using var _provider = _services.BuildServiceProvider();
        var _authorization = _provider.GetRequiredService<IAuthorizationService>();

        Assert.True((await _authorization.AuthorizeAsync(_principal, null, "Reader")).Succeeded);
        Assert.False((await _authorization.AuthorizeAsync(_principal, null, "Administrator")).Succeeded);
        var _withoutKeyService = new UserAuthenticationService(_factory, new EphemeralDataProtectionProvider(),
            new BlazorAuthConfiguration(), new UserAttemptLimiter(_clock), _clock);
        Assert.False(await _withoutKeyService.IsCurrent(_principal, default));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("id")]
    [InlineData("hash")]
    [InlineData("missing-source")]
    [InlineData("missing-id")]
    [InlineData("missing-hash")]
    [InlineData("missing-role")]
    [InlineData("administrator")]
    [InlineData("anonymous")]
    public async Task MissingAndMalformedSessionClaimsAreRejected(string change)
    {
        var (_, _secret) = await CreateDatabaseKey();
        var _key = Assert.IsType<AutoLoginKey>((await _keys.Authenticate(_secret, default)).Data);
        var _claims = AutoLoginKeyService.CreateClaims(_key).ToList();
        var _type = change switch
        {
            "source" or "missing-source" => AutoLoginKeyService.KEY_SOURCE,
            "id" or "missing-id" => AutoLoginKeyService.KEY_ID,
            "hash" or "missing-hash" => AutoLoginKeyService.KEY_HASH,
            "missing-role" => ClaimTypes.Role,
            _ => null
        };
        if (_type is not null)
        {
            _claims.RemoveAll(claim => claim.Type == _type);
            if (!change.StartsWith("missing-", StringComparison.Ordinal))
            {
                _claims.Add(new Claim(_type, "invalid"));
            }
        }
        if (change == "administrator")
        {
            _claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }
        var _principal = new ClaimsPrincipal(new ClaimsIdentity(_claims, change == "anonymous" ? null : "test"));

        Assert.False(await _keys.IsCurrent(_principal, default));
        Assert.False(await _authentication.IsCurrent(_principal, default));
    }

    private async Task<(IngestionApplication Application, string Secret)> CreateDatabaseKey(bool isMcpReadKey = false)
    {
        var _generated = IngestionKeyGenerator.Generate();
        var _application = new IngestionApplication
        {
            Name = isMcpReadKey ? "MCP client" : "Ingestion client",
            KeyHash = _generated.Hash,
            KeyPrefix = _generated.Prefix,
            IsMcpReadKey = isMcpReadKey
        };
        await using var _context = _factory.CreateDbContext();
        _context.IngestionApplications.Add(_application);
        await _context.SaveChangesAsync();
        return (_application, _generated.PlainText);
    }

    private static ClaimsPrincipal CreatePrincipal(AutoLoginKey key) => new(new ClaimsIdentity(AutoLoginKeyService.CreateClaims(key), "test"));
}
