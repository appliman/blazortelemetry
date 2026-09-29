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

internal sealed class QueryTelemetryRequestHandler(
    IDbContextFactory<TelemetryDbContext> contextFactory,
    BlazorTelemetryOptions options,
    ILogger<QueryTelemetryRequestHandler> logger) : IRequestHandler<QueryTelemetryRequest, QueryResult<TelemetryPage>>
{
    public async Task<QueryResult<TelemetryPage>> Handle(QueryTelemetryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var value = await Query(request.Query, cancellationToken);
            return new QueryResult<TelemetryPage> { Data = value };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Query failed.");
            return new QueryResult<TelemetryPage> { BrokenRules = [new BrokenRule("storage_error", string.Empty, "The operation failed.")] };
        }
    }

    private async Task<TelemetryPage> Query(TelemetryQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, options.MaximumQueryRows);
        var skip = Math.Max(0, query.Skip);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = query.RequireHttpRequestDetails
            ? context.TelemetryItems.FromSqlRaw("""
                SELECT * FROM "TelemetryItems"
                WHERE "Body" IS NOT NULL
                  AND TRIM("Body") NOT IN ('', '—', 'Blazor navigation')
                  AND CASE WHEN json_valid("AttributesJson") THEN
                      COALESCE(
                          NULLIF(TRIM(json_extract("AttributesJson", '$."client.address"'), ' —'), ''),
                          NULLIF(TRIM(json_extract("AttributesJson", '$."network.peer.address"'), ' —'), ''),
                          NULLIF(TRIM(json_extract("AttributesJson", '$."http.client_ip"'), ' —'), ''),
                          NULLIF(TRIM(json_extract("AttributesJson", '$."net.sock.peer.addr"'), ' —'), ''),
                          NULLIF(TRIM(json_extract("AttributesJson", '$."net.peer.ip"'), ' —'), '')
                      ) IS NOT NULL
                  ELSE 0 END
                """).AsNoTracking()
            : context.TelemetryItems.AsNoTracking().AsQueryable();

        if (query.Kind.HasValue)
        {
            source = source.Where(item => item.Kind == query.Kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            source = source.Where(item => item.Name == query.Name);
        }

        if (!string.IsNullOrWhiteSpace(query.ServiceName))
        {
            source = source.Where(item => item.ServiceName == query.ServiceName);
        }

        if (!string.IsNullOrWhiteSpace(query.ExcludedServiceName))
        {
            source = source.Where(item => item.ServiceName != query.ExcludedServiceName);
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            source = source.Where(item => item.TraceId == query.TraceId);
        }

        if (query.MinimumSeverityNumber.HasValue)
        {
            var minimumSeverityNumber = query.MinimumSeverityNumber.Value;
            source = source.Where(item => item.SeverityNumber >= minimumSeverityNumber);
        }

        if (query.MaximumSeverityNumber.HasValue)
        {
            var maximumSeverityNumber = query.MaximumSeverityNumber.Value;
            source = source.Where(item => item.SeverityNumber <= maximumSeverityNumber);
        }

        if (query.MinimumStatusCode.HasValue)
        {
            var minimumStatusCode = query.MinimumStatusCode.Value;
            source = source.Where(item => item.StatusCode >= minimumStatusCode);
        }

        if (query.MaximumStatusCode.HasValue)
        {
            var maximumStatusCode = query.MaximumStatusCode.Value;
            source = source.Where(item => item.StatusCode <= maximumStatusCode);
        }

        if (query.HasStatusCode.HasValue)
        {
            var hasStatusCode = query.HasStatusCode.Value;
            source = source.Where(item => item.StatusCode.HasValue == hasStatusCode);
        }

        if (query.FromUtc.HasValue)
        {
            var fromUtc = query.FromUtc.Value;
            source = source.Where(item => item.TimestampUtc >= fromUtc);
        }

        if (query.ToUtc.HasValue)
        {
            var toUtc = query.ToUtc.Value;
            source = source.Where(item => item.TimestampUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.Name.Contains(search) || (item.Body != null && item.Body.Contains(search)) || item.AttributesJson.Contains(search));
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(item => item.TimestampUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new TelemetryPage(items, total, total > skip + items.Count);
    }
}
