using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Configuration;

namespace BlazorTelemetry.Tests;

public sealed class TelemetryOptionsOverrideStoreTests
{
    [Fact]
    public async Task SavesAllowedOverridesAndPreservesExcludedSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var connectionString = $"Data Source={Path.Combine(directory, "telemetry.db")}";
            var store = new TelemetryOptionsOverrideStore(connectionString);
            var source = new BlazorTelemetryOptions
            {
                ConnectionString = connectionString,
                ReaderPolicy = "ReaderOnly",
                AdministratorPolicy = "AdminsOnly",
                IngestionKeys = new Dictionary<string, string> { ["external"] = "key" },
                Smtp = new SmtpOptions { Password = "configured-password" },
                Ntfy = new NtfyOptions { AccessToken = "configured-token" }
            };
            var values = TelemetryOptionsOverrides.From(source);
            values.RawRetentionDays = 14;
            values.QueueCapacity = 24;
            values.RequireIngestionKey = true;
            values.SensitiveAttributePatterns = ["secret", "private"];

            await store.Save(values, "new-webhook-token", false, null, false, null, true, CancellationToken.None);

            var savedText = await File.ReadAllTextAsync(Path.Combine(directory, "blazor-telemetry-options.json"));
            Assert.DoesNotContain(nameof(BlazorTelemetryOptions.ConnectionString), savedText);
            Assert.DoesNotContain(nameof(BlazorTelemetryOptions.ReaderPolicy), savedText);
            Assert.DoesNotContain(nameof(BlazorTelemetryOptions.AdministratorPolicy), savedText);
            Assert.DoesNotContain(nameof(BlazorTelemetryOptions.IngestionKeys), savedText);

            var editable = Assert.IsType<TelemetryOptionsOverrides>(store.GetForEditing());
            Assert.Equal(14, editable.RawRetentionDays);
            Assert.Null(editable.Webhook.BearerToken);
            Assert.Null(editable.Smtp.Password);
            editable.MetricRetentionDays = 60;
            await store.Save(editable, null, false, null, false, null, false, CancellationToken.None);

            var restartedStore = new TelemetryOptionsOverrideStore(connectionString);
            restartedStore.Apply(source);
            Assert.Equal(14, source.RawRetentionDays);
            Assert.Equal(60, source.MetricRetentionDays);
            Assert.Equal(24, source.QueueCapacity);
            Assert.True(source.RequireIngestionKey);
            Assert.Equal(["secret", "private"], source.SensitiveAttributePatterns);
            Assert.Equal("new-webhook-token", source.Webhook.BearerToken);
            Assert.Equal("configured-password", source.Smtp.Password);
            Assert.Equal(string.Empty, source.Ntfy.AccessToken);
            Assert.Equal(connectionString, source.ConnectionString);
            Assert.Equal("ReaderOnly", source.ReaderPolicy);
            Assert.Equal("AdminsOnly", source.AdministratorPolicy);
            Assert.Equal("key", source.IngestionKeys["external"]);

            restartedStore.Reset();
            Assert.False(restartedStore.HasOverrides);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RejectsInvalidValuesWithoutCreatingAnOverrideFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new TelemetryOptionsOverrideStore($"Data Source={Path.Combine(directory, "telemetry.db")}");
            var values = TelemetryOptionsOverrides.From(new BlazorTelemetryOptions());
            values.RawRetentionDays = 0;

            await Assert.ThrowsAsync<ArgumentException>(() => store.Save(values, null, false, null, false, null, false, CancellationToken.None));
            Assert.False(store.HasOverrides);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
