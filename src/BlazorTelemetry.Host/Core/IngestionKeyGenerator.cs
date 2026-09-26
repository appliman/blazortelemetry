using System.Security.Cryptography;
using System.Text;

namespace BlazorTelemetry.Core;

public static class IngestionKeyGenerator
{
    public static (string PlainText, string Hash, string Prefix) Generate()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var plainText = $"bt_{secret}";
        var hash = ComputeHash(plainText);
        return (plainText, hash, plainText[..11]);
    }

    public static string ComputeHash(string plainText)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));
    }
}
