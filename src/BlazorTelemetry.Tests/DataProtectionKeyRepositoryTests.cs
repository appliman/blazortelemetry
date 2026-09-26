using BlazorTelemetry.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            using (var services = CreateServices(databasePath))
            {
                var provider = services.GetRequiredService<IDataProtectionProvider>();
                payload = provider.CreateProtector("cookie").Protect("session");
            }

            using var restarted = CreateServices(databasePath);
            var restartedProvider = restarted.GetRequiredService<IDataProtectionProvider>();
            Assert.Equal("session", restartedProvider.CreateProtector("cookie").Unprotect(payload));

            await using var finalContext = new TelemetryDbContext(options);
            Assert.Single(await finalContext.DataProtectionKeys.ToListAsync());
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    private static ServiceProvider CreateServices(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TelemetryDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Pooling=False"));
        services.AddSingleton<DataProtectionKeyRepository>();
        services.AddDataProtection().SetApplicationName("BlazorTelemetry.Host");
        services.AddOptions<KeyManagementOptions>()
            .Configure<DataProtectionKeyRepository>((keyOptions, repository) => keyOptions.XmlRepository = repository);
        return services.BuildServiceProvider();
    }
}
