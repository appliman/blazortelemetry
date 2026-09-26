using System.Xml.Linq;
using BlazorTelemetry.Host.Contracts.Models.DataProtection;
using ChannelMediator;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace BlazorTelemetry.Host.DataProtection;

public sealed class DataProtectionKeyRepository(IMediator mediator) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var result = mediator.Send(new GetDataProtectionKeysRequest()).GetAwaiter().GetResult();
        if (result.HasError || result.Data is null)
        {
            throw new InvalidOperationException(result.BrokenRules.FirstOrDefault()?.Message ?? "Failed to read data protection keys.");
        }

        return result.Data.Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        var result = mediator.Send(new StoreDataProtectionKeyRequest(friendlyName, element.ToString(SaveOptions.DisableFormatting)))
            .GetAwaiter().GetResult();
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }
}
