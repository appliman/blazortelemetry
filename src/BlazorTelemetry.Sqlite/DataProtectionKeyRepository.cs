using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Sqlite;

public sealed class DataProtectionKeyRepository(IDbContextFactory<TelemetryDbContext> dbContextFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.DataProtectionKeys
            .AsNoTracking()
            .Select(key => key.Xml)
            .ToList()
            .Select(xml => XElement.Parse(xml!))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var context = dbContextFactory.CreateDbContext();
        context.DataProtectionKeys.Add(new DataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        });
        context.SaveChanges();
    }
}
