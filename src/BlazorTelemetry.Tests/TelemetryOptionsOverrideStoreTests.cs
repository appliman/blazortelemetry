using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorTelemetry.Tests;

public sealed class TelemetryOptionsOverrideStoreTests
{
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task SavesAllowedOverridesAndPreservesExcludedSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var connectionString = $"Data Source={Path.Combine(directory, "telemetry.db")}";
            var store = new TelemetryOptionsOverrideStore(connectionString, _protection);
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
            Assert.DoesNotContain("new-webhook-token", savedText);
            Assert.Contains("protected:v1:", savedText);

            var editable = Assert.IsType<TelemetryOptionsOverrides>(store.GetForEditing());
            Assert.Equal(14, editable.RawRetentionDays);
            Assert.Null(editable.Webhook.BearerToken);
            Assert.Null(editable.Smtp.Password);
            editable.MetricRetentionDays = 60;
            await store.Save(editable, null, false, null, false, null, false, CancellationToken.None);

            var restartedStore = new TelemetryOptionsOverrideStore(connectionString, _protection);
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
    public async Task MigratesPlaintextSecretsAndDoesNotExposeThemWhenEditing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "blazor-telemetry-options.json");
            var values = TelemetryOptionsOverrides.From(new BlazorTelemetryOptions());
            values.Webhook.BearerToken = "legacy-webhook-secret";
            values.Smtp.Password = "legacy-smtp-secret";
            values.Ntfy.AccessToken = "legacy-ntfy-secret";
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(values));

            var connectionString = $"Data Source={Path.Combine(directory, "telemetry.db")}";
            var store = new TelemetryOptionsOverrideStore(connectionString, _protection);
            var text = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("legacy-webhook-secret", text);
            Assert.DoesNotContain("legacy-smtp-secret", text);
            Assert.DoesNotContain("legacy-ntfy-secret", text);

            var options = new BlazorTelemetryOptions();
            store.Apply(options);
            Assert.Equal("legacy-webhook-secret", options.Webhook.BearerToken);
            Assert.Equal("legacy-smtp-secret", options.Smtp.Password);
            Assert.Equal("legacy-ntfy-secret", options.Ntfy.AccessToken);
            var editable = Assert.IsType<TelemetryOptionsOverrides>(store.GetForEditing());
            Assert.Null(editable.Webhook.BearerToken);
            Assert.Null(editable.Smtp.Password);
            Assert.Null(editable.Ntfy.AccessToken);

            Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
                new TelemetryOptionsOverrideStore(connectionString, new EphemeralDataProtectionProvider()));
            Assert.Equal(text, await File.ReadAllTextAsync(path));
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
            var store = new TelemetryOptionsOverrideStore($"Data Source={Path.Combine(directory, "telemetry.db")}", _protection);
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
