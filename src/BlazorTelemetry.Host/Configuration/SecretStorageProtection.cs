using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace BlazorTelemetry.Host.Configuration;

public sealed class SecretStorageProtection(IDataProtectionProvider provider) : IDisposable
{
    private X509Certificate2? _certificate;

    public IDataProtectionProvider Provider { get; } = provider;

    public static SecretStorageProtection Create(IConfigurationSection configuration, bool development, string connectionString)
    {
        var certificatePath = configuration["CertificatePath"];
        var keyDirectory = configuration["KeyDirectory"];
        var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource))!;
        if (string.IsNullOrWhiteSpace(certificatePath) && (!development || !OperatingSystem.IsWindows()))
        {
            throw new InvalidOperationException("SecretStorage:CertificatePath must identify an external private-key certificate.");
        }

        if (string.IsNullOrWhiteSpace(keyDirectory))
        {
            if (!development)
            {
                throw new InvalidOperationException("SecretStorage:KeyDirectory must identify a persistent directory outside the database directory.");
            }

            keyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorTelemetry", "SecretStorageKeys");
        }

        ValidateExternalPath(keyDirectory, databaseDirectory);
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            ValidateExternalPath(certificatePath, databaseDirectory);
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, configuration["CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is null || rsa.KeySize < 2048)
            {
                certificate.Dispose();
                throw new InvalidOperationException("The secret storage certificate must contain an RSA private key of at least 2048 bits.");
            }

            // The provider retains the certificate for decrypting its persisted key ring.
            try
            {
                var protectedProvider = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory), builder =>
                    builder.SetApplicationName("BlazorTelemetry.SecretStorage.v1").ProtectKeysWithCertificate(certificate));
                return Validate(new SecretStorageProtection(protectedProvider) { _certificate = certificate });
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Local secret storage without a certificate requires Windows DPAPI.");
        }

        var localProvider = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory), builder =>
        {
            builder.SetApplicationName("BlazorTelemetry.SecretStorage.v1");
            if (OperatingSystem.IsWindows())
            {
                builder.ProtectKeysWithDpapi();
            }
            else
            {
                throw new PlatformNotSupportedException("Local secret storage without a certificate requires Windows DPAPI.");
            }
        });
        return Validate(new SecretStorageProtection(localProvider));
    }

    public void Dispose()
    {
        if (Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _certificate?.Dispose();
    }

    private static SecretStorageProtection Validate(SecretStorageProtection storage)
    {
        try
        {
            var protector = storage.Provider.CreateProtector("BlazorTelemetry.SecretStorage.Validation.v1");
            var protectedValue = protector.Protect("validation");
            if (protector.Unprotect(protectedValue) != "validation")
            {
                throw new CryptographicException("Secret storage verification failed.");
            }

            return storage;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            storage.Dispose();
            throw new InvalidOperationException("Secret storage could not be initialized. Check key directory permissions and certificate configuration.", exception);
        }
    }

    private static void ValidateExternalPath(string path, string databaseDirectory)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(databaseDirectory, fullPath);
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Secret storage certificates and keys must be stored outside the database directory.");
        }
    }
}
