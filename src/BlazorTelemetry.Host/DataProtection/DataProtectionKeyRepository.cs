using System.Xml.Linq;
using BlazorTelemetry.Host.Contracts.Models.DataProtection;
using ChannelMediator;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;
using BlazorTelemetry.Host.Configuration;

namespace BlazorTelemetry.Host.DataProtection;

public sealed class DataProtectionKeyRepository(IMediator mediator, SecretStorageProtection storage) : IXmlRepository
{
    private readonly IDataProtector _protector = storage.Provider.CreateProtector("BlazorTelemetry.DatabaseKeys.v1");

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var result = mediator.Send(new GetDataProtectionKeysRequest()).GetAwaiter().GetResult();
        if (result.HasError || result.Data is null)
        {
            throw new InvalidOperationException(result.BrokenRules.FirstOrDefault()?.Message ?? "Failed to read data protection keys.");
        }

        return result.Data.Select(value => Unprotect(XElement.Parse(value))).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        var result = mediator.Send(new StoreDataProtectionKeyRequest(friendlyName, Protect(element).ToString(SaveOptions.DisableFormatting)))
            .GetAwaiter().GetResult();
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

    public async Task ProtectExistingKeys(IDbContextFactory<TelemetryDbContext> contextFactory, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var keys = await context.DataProtectionKeys.ToListAsync(cancellationToken);
        var changed = false;
        foreach (var key in keys)
        {
            var element = XElement.Parse(key.Xml!);
            if (element.Name == "protectedKey")
            {
                _ = Unprotect(element);
                continue;
            }

            key.Xml = Protect(element).ToString(SaveOptions.DisableFormatting);
            changed = true;
        }

        if (changed)
        {
            // Remove replaced plaintext from SQLite pages and checkpoint the old WAL before backups can run.
            await context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA secure_delete=ON", cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", cancellationToken);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    private XElement Protect(XElement element)
    {
        return new XElement("protectedKey", _protector.Protect(element.ToString(SaveOptions.DisableFormatting)));
    }

    private XElement Unprotect(XElement element)
    {
        return element.Name == "protectedKey" ? XElement.Parse(_protector.Unprotect(element.Value)) : element;
    }
}
