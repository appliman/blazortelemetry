using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using ChannelMediator;
using ChannelMediator.InMemory;
using BlazorTelemetry.Host.ModelExtensions;
using BlazorTelemetry.Host.Contracts.Models.Maintenance;
using BlazorTelemetry.Host.Validators;
using FluentValidation;
using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;

namespace BlazorTelemetry.Tests;

public sealed partial class TelemetryCqrsTests
{
    [Fact]
    public async Task InvalidApplicationReturnsBrokenRulesWithoutWriting()
    {
        var result = await _mediator.Send(new SaveIngestionApplicationRequest(new IngestionApplication()), CancellationToken.None);

        Assert.True(result.HasError);
        Assert.Contains(result.BrokenRules, rule => rule.PropertyName == nameof(IngestionApplication.Name));
        await using var context = new TelemetryDbContext(_options);
        Assert.Empty(await context.IngestionApplications.ToListAsync());
    }

    [Fact]
    public async Task McpKeyCannotIngestAndIngestionKeyCannotRead()
    {
        var repository = _mediator;
        var ingestion = IngestionKeyGenerator.Generate();
        var mcp = IngestionKeyGenerator.Generate();
        await repository.SaveIngestionApplication(new IngestionApplication
        {
            Name = "ingestion",
            KeyHash = ingestion.Hash,
            KeyPrefix = ingestion.Prefix
        }, CancellationToken.None);
        await repository.SaveIngestionApplication(new IngestionApplication
        {
            Name = "mcp",
            KeyHash = mcp.Hash,
            KeyPrefix = mcp.Prefix,
            IsMcpReadKey = true
        }, CancellationToken.None);

        Assert.NotNull(await repository.FindActiveIngestionApplication(ingestion.Hash, CancellationToken.None));
        Assert.Null(await repository.FindActiveMcpReadKey(ingestion.Hash, CancellationToken.None));
        Assert.Null(await repository.FindActiveIngestionApplication(mcp.Hash, CancellationToken.None));
        var active = await repository.FindActiveMcpReadKey(mcp.Hash, CancellationToken.None);
        Assert.NotNull(active);
        Assert.True(await repository.RevokeIngestionApplication(active.Id, CancellationToken.None));
        Assert.Null(await repository.FindActiveMcpReadKey(mcp.Hash, CancellationToken.None));
    }

    [Fact]
    public async Task RevokedIngestionApplicationCannotBeResolved()
    {
        var repository = _mediator;
        var application = new IngestionApplication { Name = "api", KeyHash = "HASH", KeyPrefix = "bt_test" };
        await repository.SaveIngestionApplication(application, CancellationToken.None);

        Assert.NotNull(await repository.FindActiveIngestionApplication("HASH", CancellationToken.None));
        application.IsActive = false;
        await repository.SaveIngestionApplication(application, CancellationToken.None);

        Assert.Null(await repository.FindActiveIngestionApplication("HASH", CancellationToken.None));
    }

    [Fact]
    public async Task IngestionKeyLifecycleTracksUsageExpiryRotationAndRevocation()
    {
        var repository = _mediator;
        var generated = IngestionKeyGenerator.Generate();
        var original = new IngestionApplication
        {
            Name = "api",
            KeyHash = generated.Hash,
            KeyPrefix = generated.Prefix,
            AllowAutoLogin = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        await repository.SaveIngestionApplication(original, CancellationToken.None);

        Assert.Equal(generated.Hash, IngestionKeyGenerator.ComputeHash(generated.PlainText));
        Assert.DoesNotContain(generated.PlainText, generated.Hash, StringComparison.Ordinal);
        Assert.NotNull(await repository.FindActiveIngestionApplication(generated.Hash, CancellationToken.None));
        Assert.NotNull(await repository.FindActiveIngestionApplication(generated.Hash, CancellationToken.None));
        var used = await repository.GetIngestionApplication(original.Id, CancellationToken.None);
        Assert.Equal(2, used?.UsageCount);
        Assert.NotNull(used?.LastSeenUtc);

        var replacementKey = IngestionKeyGenerator.Generate();
        var replacement = new IngestionApplication { KeyHash = replacementKey.Hash, KeyPrefix = replacementKey.Prefix };
        Assert.True(await repository.RotateIngestionApplication(original.Id, replacement, CancellationToken.None));
        Assert.Null(await repository.FindActiveIngestionApplication(generated.Hash, CancellationToken.None));
        Assert.NotNull(await repository.FindActiveIngestionApplication(replacementKey.Hash, CancellationToken.None));
        Assert.Equal(used?.ExpiresUtc, (await repository.GetIngestionApplication(replacement.Id, CancellationToken.None))?.ExpiresUtc);
        Assert.True((await repository.GetIngestionApplication(replacement.Id, CancellationToken.None))?.AllowAutoLogin);
        Assert.False(await repository.RotateIngestionApplication(original.Id, new IngestionApplication(), CancellationToken.None));
        Assert.True(await repository.RevokeIngestionApplication(replacement.Id, CancellationToken.None));
        Assert.Null(await repository.FindActiveIngestionApplication(replacementKey.Hash, CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredIngestionKeyIsRejected()
    {
        var repository = _mediator;
        var expired = new IngestionApplication
        {
            Name = "expired",
            KeyHash = "EXPIRED",
            KeyPrefix = "bt_expired",
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        await repository.SaveIngestionApplication(expired, CancellationToken.None);

        Assert.Null(await repository.FindActiveIngestionApplication(expired.KeyHash, CancellationToken.None));
        Assert.Equal(0, (await repository.GetIngestionApplication(expired.Id, CancellationToken.None))?.UsageCount);

        var replacement = new IngestionApplication { KeyHash = "REPLACEMENT", KeyPrefix = "bt_new" };
        Assert.True(await repository.RotateIngestionApplication(expired.Id, replacement, CancellationToken.None));
        Assert.NotNull(await repository.FindActiveIngestionApplication(replacement.KeyHash, CancellationToken.None));
    }

    [Fact]
    public async Task IngestionOnlyKeyCannotAutoLoginAndPermissionCanBeGrantedAndRemoved()
    {
        var _generated = IngestionKeyGenerator.Generate();
        var _application = new IngestionApplication
        {
            Name = "Ingestion only",
            KeyHash = _generated.Hash,
            KeyPrefix = _generated.Prefix
        };
        await _mediator.SaveIngestionApplication(_application, CancellationToken.None);

        Assert.NotNull(await _mediator.FindActiveIngestionApplication(_generated.Hash, CancellationToken.None));
        Assert.Null((await _mediator.Send(new ValidateAutoLoginKeyRequest(_generated.Hash), CancellationToken.None)).Data);

        _application.AllowAutoLogin = true;
        await _mediator.SaveIngestionApplication(_application, CancellationToken.None);
        Assert.NotNull((await _mediator.Send(new ValidateAutoLoginKeyRequest(_generated.Hash), CancellationToken.None)).Data);
        Assert.NotNull(await _mediator.FindActiveIngestionApplication(_generated.Hash, CancellationToken.None));

        _application.AllowAutoLogin = false;
        await _mediator.SaveIngestionApplication(_application, CancellationToken.None);
        Assert.Null((await _mediator.Send(new ValidateAutoLoginKeyRequest(_generated.Hash), CancellationToken.None)).Data);
        Assert.NotNull(await _mediator.FindActiveIngestionApplication(_generated.Hash, CancellationToken.None));
    }

    [Fact]
    public async Task SecurityMigrationPreservesExistingKeysAndUsersWithoutGrantingAutoLogin()
    {
        var _path = Path.Combine(Path.GetTempPath(), $"telemetry-security-upgrade-{Guid.NewGuid():N}.db");
        var _options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options;
        try
        {
            await using var _context = new TelemetryDbContext(_options);
            await _context.GetService<IMigrator>().MigrateAsync("20261006145437_AddTelemetryUsers");
            await _context.Database.ExecuteSqlRawAsync("INSERT INTO IngestionApplications (Name,KeyHash,KeyPrefix,IsActive,IsMcpReadKey,CreatedUtc,UsageCount) VALUES ('Existing app','existing-hash','prefix',1,0,0,19)");
            var _securityVersion = Guid.NewGuid();
            await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Identifier,ProtectedSecret,IsActive,SecurityVersion,LastAcceptedInterval) VALUES ('existing-user','protected-secret',1,{_securityVersion},10)");

            await _context.Database.MigrateAsync();

            var _key = await _context.IngestionApplications.SingleAsync();
            Assert.False(_key.AllowAutoLogin);
            Assert.Equal("existing-hash", _key.KeyHash);
            Assert.Equal(19, _key.UsageCount);
            var _user = await _context.Users.SingleAsync();
            Assert.Equal("protected-secret", _user.ProtectedSecret);
            Assert.Equal(_securityVersion, _user.SecurityVersion);
            Assert.Equal(10, _user.LastAcceptedInterval);
            Assert.Empty(await _context.AdministratorTotpStates.ToListAsync());
        }
        finally
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task UpdatingAutoLoginPermissionDoesNotRestoreARevokedOrRotatedKey()
    {
        var _generated = IngestionKeyGenerator.Generate();
        var _application = new IngestionApplication
        {
            Name = "Permission race",
            KeyHash = _generated.Hash,
            KeyPrefix = _generated.Prefix
        };
        await _mediator.SaveIngestionApplication(_application, CancellationToken.None);
        var _enabled = await _mediator.Send(new SetIngestionApplicationAutoLoginRequest(_application.Id, true), CancellationToken.None);
        Assert.Equal(1, _enabled.ChangeCount);

        var _replacement = IngestionKeyGenerator.Generate();
        var _replacementApplication = new IngestionApplication { KeyHash = _replacement.Hash, KeyPrefix = _replacement.Prefix };
        Assert.True(await _mediator.RotateIngestionApplication(_application.Id, _replacementApplication, CancellationToken.None));

        var _staleUpdate = await _mediator.Send(new SetIngestionApplicationAutoLoginRequest(_application.Id, true), CancellationToken.None);
        Assert.False(_staleUpdate.HasError);
        Assert.Equal(0, _staleUpdate.ChangeCount);
        Assert.Null(await _mediator.FindActiveIngestionApplication(_generated.Hash, CancellationToken.None));
        Assert.NotNull(await _mediator.FindActiveIngestionApplication(_replacement.Hash, CancellationToken.None));

        Assert.True(await _mediator.RevokeIngestionApplication(_replacementApplication.Id, CancellationToken.None));
        Assert.Equal(0, (await _mediator.Send(new SetIngestionApplicationAutoLoginRequest(_replacementApplication.Id, true), CancellationToken.None)).ChangeCount);
        Assert.Null(await _mediator.FindActiveIngestionApplication(_replacement.Hash, CancellationToken.None));
    }

}
