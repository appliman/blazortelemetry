using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.DataProtection;
using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using ChannelMediator;
using ChannelMediator.InMemory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BlazorTelemetry.Host.Configuration;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace BlazorTelemetry.Tests;

public sealed class DataProtectionKeyRepositoryTests
{
    [Fact]
    public async Task KeysPersistInSqliteAcrossProviderRecreation()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var databasePath = Path.Combine(folder, "telemetry.db");
            var options = new DbContextOptionsBuilder<TelemetryDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using (var context = new TelemetryDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            string payload;
            var storage = new SecretStorageProtection(new EphemeralDataProtectionProvider());
            using (var services = CreateServices(databasePath, storage))
            {
                var provider = services.GetRequiredService<IDataProtectionProvider>();
                payload = provider.CreateProtector("cookie").Protect("session");
            }

            using var restarted = CreateServices(databasePath, storage);
            var restartedProvider = restarted.GetRequiredService<IDataProtectionProvider>();
            Assert.Equal("session", restartedProvider.CreateProtector("cookie").Unprotect(payload));

            await using var finalContext = new TelemetryDbContext(options);
            var key = Assert.Single(await finalContext.DataProtectionKeys.ToListAsync());
            Assert.StartsWith("<protectedKey>", key.Xml);
            Assert.DoesNotContain("masterKey", key.Xml);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task MigratingExistingKeysPreservesProtectedSessionsAndRejectsAnUnrelatedStorageKey()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var databasePath = Path.Combine(folder, "telemetry.db");
            var options = new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite($"Data Source={databasePath};Pooling=False").Options;
            var legacyDirectory = Path.Combine(folder, "legacy-keys");
            var legacyProvider = DataProtectionProvider.Create(new DirectoryInfo(legacyDirectory), builder => builder.SetApplicationName("BlazorTelemetry.Host"));
            var payload = legacyProvider.CreateProtector("cookie").Protect("legacy-session");
            await using (var context = new TelemetryDbContext(options))
            {
                await context.Database.MigrateAsync();
                foreach (var file in Directory.GetFiles(legacyDirectory, "*.xml"))
                {
                    context.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = Path.GetFileName(file), Xml = await File.ReadAllTextAsync(file) });
                }

                await context.SaveChangesAsync();
            }

            var storage = new SecretStorageProtection(new EphemeralDataProtectionProvider());
            using (var services = CreateServices(databasePath, storage))
            {
                var repository = services.GetRequiredService<DataProtectionKeyRepository>();
                var factory = services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
                await repository.ProtectExistingKeys(factory, CancellationToken.None);
                await repository.ProtectExistingKeys(factory, CancellationToken.None);
                Assert.Equal("legacy-session", services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie").Unprotect(payload));
            }

            await using var finalContext = new TelemetryDbContext(options);
            Assert.All(await finalContext.DataProtectionKeys.ToListAsync(), key =>
            {
                Assert.StartsWith("<protectedKey>", key.Xml);
                Assert.DoesNotContain("masterKey", key.Xml);
            });

            using var unrelated = CreateServices(databasePath, new SecretStorageProtection(new EphemeralDataProtectionProvider()));
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() => unrelated.GetRequiredService<DataProtectionKeyRepository>().GetAllElements());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static ServiceProvider CreateServices(string databasePath, SecretStorageProtection storage)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TelemetryDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Pooling=False"));
        services.AddLogging();
        services.AddSingleton(storage);
        services.AddSingleton(new BlazorTelemetryOptions());
        services.AddChannelMediator(configuration => configuration.UseChannelMediatorInMemory(), typeof(BlazorTelemetryServiceCollectionExtensions).Assembly);
        services.AddSingleton<DataProtectionKeyRepository>();
        services.AddDataProtection().SetApplicationName("BlazorTelemetry.Host");
        services.AddOptions<KeyManagementOptions>()
            .Configure<DataProtectionKeyRepository>((keyOptions, repository) => keyOptions.XmlRepository = repository);
        return services.BuildServiceProvider();
    }
}
