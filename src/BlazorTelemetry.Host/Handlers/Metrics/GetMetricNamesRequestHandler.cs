using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using static BlazorTelemetry.Host.Handlers.Metrics.MetricQueryHelpers;

using BlazorTelemetry.Host.Contracts.Models.Metrics;

namespace BlazorTelemetry.Host.Handlers.Metrics;

internal sealed class GetMetricNamesRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    ILogger<GetMetricNamesRequestHandler> logger) : IRequestHandler<GetMetricNamesRequest, QueryResult<IReadOnlyList<string>>>
{
    public async Task<QueryResult<IReadOnlyList<string>>> Handle(GetMetricNamesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await GetMetricNames(request.FromUtc, request.ToUtc, request.ServiceName, cancellationToken);
            return new QueryResult<IReadOnlyList<string>> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "GetMetricNames failed.");
            return new QueryResult<IReadOnlyList<string>> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<IReadOnlyList<string>> GetMetricNames(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? serviceName, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = context.TelemetryItems.AsNoTracking()
            .Where(item => item.Kind == TelemetryKind.Metric && item.TimestampUtc >= fromUtc && item.TimestampUtc <= toUtc);
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            source = source.Where(item => item.ServiceName == serviceName);
        }

        return await source.Select(item => item.Name).Distinct().OrderBy(name => name).Take(500).ToListAsync(cancellationToken);
    }
}
