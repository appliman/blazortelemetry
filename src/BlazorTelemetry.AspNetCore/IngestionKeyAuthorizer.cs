using System.Security.Cryptography;
using System.Text;
using BlazorTelemetry.Core;

namespace BlazorTelemetry.AspNetCore;

internal sealed class IngestionKeyAuthorizer(BlazorTelemetryOptions options, IServiceScopeFactory scopeFactory)
{
    public async Task<(bool IsAuthorized, string? ApplicationName)> Authorize(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!options.RequireIngestionKey)
        {
            return (true, null);
        }

        if (!request.Headers.TryGetValue("X-BlazorTelemetry-Key", out var values))
        {
            return (false, null);
        }

        var supplied = values.ToString();
        foreach (var pair in options.IngestionKeys)
        {
            if (FixedTimeEquals(pair.Value, supplied))
            {
                return (true, pair.Key);
            }
        }

        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var application = await repository.FindActiveIngestionApplication(keyHash, cancellationToken);
        return application is null ? (false, null) : (true, application.Name);
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
