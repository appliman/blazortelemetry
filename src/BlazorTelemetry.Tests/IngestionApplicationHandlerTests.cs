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

}
