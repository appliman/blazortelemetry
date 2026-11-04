using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BlazorTelemetry.Host.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace BlazorTelemetry.Tests;

public sealed class SecretStorageProtectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CertificateIsOptionalAndDefaultKeysSurviveRestart(bool development)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder().Build();
            var connectionString = $"Data Source={Path.Combine(directory, "telemetry.db")}";
            string encrypted;
            using (var storage = SecretStorageProtection.Create(configuration.GetSection("SecretStorage"), development, connectionString))
            {
                encrypted = storage.Provider.CreateProtector("test").Protect("persistent-secret");
            }

            using var restarted = SecretStorageProtection.Create(configuration.GetSection("SecretStorage"), development, connectionString);
            Assert.Equal("persistent-secret", restarted.Provider.CreateProtector("test").Unprotect(encrypted));
            if (!development || !OperatingSystem.IsWindows())
            {
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(directory, "secret-storage-keys"), "*.xml"));
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RejectsCertificateInsideDatabaseDirectory()
    {
        var databaseDirectory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-storage-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretStorage:CertificatePath"] = Path.Combine(databaseDirectory, "certificate.pfx")
        }).Build();

        Assert.Throws<InvalidOperationException>(() => SecretStorageProtection.Create(configuration.GetSection("SecretStorage"), false, $"Data Source={Path.Combine(databaseDirectory, "telemetry.db")}"));
    }

    [Fact]
    public void ExternalCertificateProtectsPersistedKeyRingAndSurvivesRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blazor-telemetry-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=BlazorTelemetry storage test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificatePath = Path.Combine(directory, "certificate.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "test-password"));
            var keyDirectory = Path.Combine(directory, "keys");
            var connectionString = $"Data Source={Path.Combine(directory, "database", "telemetry.db")}";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretStorage:CertificatePath"] = certificatePath,
                ["SecretStorage:CertificatePassword"] = "test-password",
                ["SecretStorage:KeyDirectory"] = keyDirectory
            }).Build();

            string encrypted;
            using (var storage = SecretStorageProtection.Create(configuration.GetSection("SecretStorage"), false, connectionString))
            {
                encrypted = storage.Provider.CreateProtector("test").Protect("persistent-secret");
            }

            var keyFile = Assert.Single(Directory.GetFiles(keyDirectory, "*.xml"));
            var keyXml = File.ReadAllText(keyFile);
            Assert.Contains("encryptedSecret", keyXml);
            Assert.DoesNotContain("<value>", keyXml);

            var blockedDirectory = Path.Combine(directory, "blocked-key-directory");
            File.WriteAllText(blockedDirectory, "This file cannot be used as a key directory.");
            configuration["SecretStorage:KeyDirectory"] = blockedDirectory;
            var failure = Assert.Throws<InvalidOperationException>(() =>
                SecretStorageProtection.Create(configuration.GetSection("SecretStorage"), false, connectionString));
            Assert.Contains("Check key directory permissions and certificate configuration", failure.Message);
            configuration["SecretStorage:KeyDirectory"] = keyDirectory;

            using var restarted = SecretStorageProtection.Create(configuration.GetSection("SecretStorage"), false, connectionString);
            Assert.Equal("persistent-secret", restarted.Provider.CreateProtector("test").Unprotect(encrypted));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
