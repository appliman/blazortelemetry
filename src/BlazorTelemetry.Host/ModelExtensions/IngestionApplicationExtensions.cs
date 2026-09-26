using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.IngestionApplications;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class IngestionApplicationExtensions
{
    public static async Task<IReadOnlyList<IngestionApplication>> GetIngestionApplications(this IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetIngestionApplicationsRequest(), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IngestionApplication?> FindActiveIngestionApplication(this IMediator mediator, string keyHash, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ValidateIngestionKeyRequest(keyHash), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IngestionApplication?> FindActiveMcpReadKey(this IMediator mediator, string keyHash, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ValidateMcpReadKeyRequest(keyHash), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IngestionApplication?> GetIngestionApplication(this IMediator mediator, int id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetIngestionApplicationRequest(id), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<bool> RevokeIngestionApplication(this IMediator mediator, int id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RevokeIngestionApplicationRequest(id), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.ChangeCount > 0;
    }

    public static async Task<bool> RotateIngestionApplication(this IMediator mediator, int id, IngestionApplication replacement, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RotateIngestionApplicationRequest(id, replacement), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.ChangeCount > 0;
    }

    public static async Task SaveIngestionApplication(this IMediator mediator, IngestionApplication application, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SaveIngestionApplicationRequest(application), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

}
