using System.Globalization;
using System.Security.Claims;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;
using BlazorTelemetry.Host.Contracts.Results;

namespace BlazorTelemetry.Host.Authentication;

public sealed class AutoLoginKeyService(IMediator repository, BlazorTelemetryOptions options)
{
    public const string KEY_SOURCE = "telemetry_auto_login_key_source";
    public const string KEY_ID = "telemetry_auto_login_key_id";
    public const string KEY_HASH = "telemetry_auto_login_key_hash";

    public async Task<QueryResult<AutoLoginKey?>> Authenticate(string supplied, CancellationToken cancellationToken)
    {
        var _hash = IngestionKeyGenerator.ComputeHash(supplied);
        foreach (var _entry in options.IngestionKeys)
        {
            if (IngestionKeyGenerator.ComputeHash(_entry.Value) == _hash)
            {
                return new QueryResult<AutoLoginKey?> { Data = new AutoLoginKey("configuration", _entry.Key, _hash, _entry.Key) };
            }
        }

        var _result = await repository.Send(new ValidateAutoLoginKeyRequest(_hash), cancellationToken);
        return new QueryResult<AutoLoginKey?>
        {
            BrokenRules = _result.BrokenRules,
            Data = _result.Data is { } _key
                ? new AutoLoginKey("database", _key.Id.ToString(CultureInfo.InvariantCulture), _key.KeyHash, _key.Name)
                : null
        };
    }

    public async Task<bool> IsCurrent(AutoLoginKey key, CancellationToken cancellationToken)
    {
        if (key.Source == "configuration")
        {
            return options.IngestionKeys.TryGetValue(key.Id, out var _value) && IngestionKeyGenerator.ComputeHash(_value) == key.Hash;
        }
        if (key.Source != "database")
        {
            return false;
        }

        var _result = await repository.Send(new ValidateAutoLoginKeyRequest(key.Hash), cancellationToken);
        return !_result.HasError && _result.Data is { } _key && _key.Id.ToString(CultureInfo.InvariantCulture) == key.Id;
    }

    public Task<bool> IsCurrent(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var _source = principal.FindFirstValue(KEY_SOURCE);
        var _id = principal.FindFirstValue(KEY_ID);
        var _hash = principal.FindFirstValue(KEY_HASH);
        if (principal.Identity?.IsAuthenticated != true || !principal.IsInRole("Reader") || principal.IsInRole("Administrator")
            || string.IsNullOrEmpty(_source) || string.IsNullOrEmpty(_id) || string.IsNullOrEmpty(_hash))
        {
            return Task.FromResult(false);
        }
        return IsCurrent(new AutoLoginKey(_source, _id, _hash, string.Empty), cancellationToken);
    }

    public static IReadOnlyCollection<Claim> CreateClaims(AutoLoginKey key) =>
    [
        new(ClaimTypes.NameIdentifier, $"api-key:{key.Source}:{key.Id}"),
        new(ClaimTypes.Name, key.Name),
        new(ClaimTypes.Role, "Reader"),
        new(KEY_SOURCE, key.Source),
        new(KEY_ID, key.Id),
        new(KEY_HASH, key.Hash)
    ];
}
