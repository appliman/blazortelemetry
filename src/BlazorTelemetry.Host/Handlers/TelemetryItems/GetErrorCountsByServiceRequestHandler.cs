using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.TelemetryItems;

namespace BlazorTelemetry.Host.Handlers.TelemetryItems;

internal sealed class GetErrorCountsByServiceRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetErrorCountsByServiceRequestHandler> logger) : IRequestHandler<GetErrorCountsByServiceRequest, QueryResult<IReadOnlyDictionary<string, long>>>
{
    public async Task<QueryResult<IReadOnlyDictionary<string, long>>> Handle(GetErrorCountsByServiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetErrorCountsByService(request.FromUtc, request.ServiceName, request.Search, cancellationToken);
            return new QueryResult<IReadOnlyDictionary<string, long>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetErrorCountsByService failed.");
            return new QueryResult<IReadOnlyDictionary<string, long>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyDictionary<string, long>> GetErrorCountsByService(
        DateTimeOffset fromUtc,
        string? serviceName,
        string? search,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Log && item.TimestampUtc >= fromUtc && (item.SeverityNumber ?? 0) >= 17);

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            source = source.Where(item => item.Name.Contains(normalizedSearch)
                || (item.Body != null && item.Body.Contains(normalizedSearch))
                || item.AttributesJson.Contains(normalizedSearch));
        }

        var counts = await source
            .GroupBy(item => item.ServiceName)
            .Select(group => new { ServiceName = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(item => item.ServiceName, item => item.Count, StringComparer.Ordinal);
    }
}
