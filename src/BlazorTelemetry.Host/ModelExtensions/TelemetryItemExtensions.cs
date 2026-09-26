using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class TelemetryItemExtensions
{
    public static async Task CompleteRequest(this IMediator mediator, string requestId, double durationMs, int statusCode, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CompleteRequestTelemetryRequest(requestId, durationMs, statusCode), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

    public static async Task Store(this IMediator mediator, IReadOnlyCollection<TelemetryItem> items, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new StoreTelemetryBatchRequest(items), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

    public static async Task<TelemetryPage> Query(this IMediator mediator, TelemetryQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new QueryTelemetryRequest(query), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IReadOnlyList<string>> GetServices(this IMediator mediator, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetServicesRequest(fromUtc, toUtc), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<TelemetrySummary> GetSummary(this IMediator mediator, DateTimeOffset fromUtc, CancellationToken cancellationToken, DateTimeOffset? toUtc = null, string? serviceName = null)
    {
        var result = await mediator.Send(new GetSummaryRequest(fromUtc, toUtc, serviceName), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<DashboardBreakdown> GetDashboardBreakdown(this IMediator mediator, DateTimeOffset fromUtc, string? serviceName, string? excludedRequestServiceName, CancellationToken cancellationToken, DateTimeOffset? toUtc = null)
    {
        var result = await mediator.Send(new GetDashboardBreakdownRequest(fromUtc, serviceName, excludedRequestServiceName, toUtc), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task<IReadOnlyDictionary<string, long>> GetErrorCountsByService(this IMediator mediator, DateTimeOffset fromUtc, string? serviceName, string? search, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetErrorCountsByServiceRequest(fromUtc, serviceName, search), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

}
