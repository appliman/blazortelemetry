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
        var source = context.QueryTelemetry(query.Kind);
        if (query.RequireHttpRequestDetails)
        {
            source = source.Where(_item => _item.Kind == TelemetryKind.Request
                && _item.HttpMethod != null && _item.HttpMethod.Trim() != string.Empty
                && _item.HttpMethod.Trim() != "—" && _item.HttpMethod != "Blazor navigation"
                && _item.ClientAddress != null && _item.ClientAddress.Trim() != string.Empty && _item.ClientAddress.Trim() != "—");
        }

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
            var matchingRequestNumbers = Array.Empty<int>();
            if ((!query.Kind.HasValue || query.Kind == TelemetryKind.Request)
                && search.All(_character => _character is >= '0' and <= '9' or '-'))
            {
                // Materialize distinct scalar values before formatting: no numeric conversion in SQLite LINQ.
                var numbers = await context.TelemetryRequest.Select(_item => _item.ClientPort)
                    .Concat(context.TelemetryRequest.Select(_item => _item.ServerPort))
                    .Concat(context.TelemetryRequest.Select(_item => _item.StatusCode))
                    .Where(_number => _number.HasValue).Distinct().ToListAsync(cancellationToken);
                matchingRequestNumbers = numbers.Where(_number => _number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(search, StringComparison.Ordinal))
                    .Select(_number => _number!.Value).ToArray();
            }
            source = source.Where(item => item.Name.Contains(search) || (item.Body != null && item.Body.Contains(search)) || item.AttributesJson.Contains(search)
                || (item.HttpMethod != null && item.HttpMethod.Contains(search))
                || (item.Url != null && item.Url.Contains(search))
                || (item.Route != null && item.Route.Contains(search))
                || (item.ClientAddress != null && item.ClientAddress.Contains(search))
                || (item.ServerAddress != null && item.ServerAddress.Contains(search))
                || (item.UserAgent != null && item.UserAgent.Contains(search))
                || (item.ProtocolVersion != null && item.ProtocolVersion.Contains(search))
                || (item.UrlScheme != null && item.UrlScheme.Contains(search))
                || (item.CircuitId != null && item.CircuitId.Contains(search))
                || (item.Kind == TelemetryKind.Request
                    && ((item.ClientPort.HasValue && EF.Parameter(matchingRequestNumbers).Contains(item.ClientPort.Value))
                        || (item.ServerPort.HasValue && EF.Parameter(matchingRequestNumbers).Contains(item.ServerPort.Value))
                        || (item.StatusCode.HasValue && EF.Parameter(matchingRequestNumbers).Contains(item.StatusCode.Value)))));
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(item => item.TimestampUtc).ThenBy(item => item.Kind).ThenByDescending(item => item.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        await context.LoadTelemetryChildren(items, cancellationToken);
        return new TelemetryPage(items, total, total > skip + items.Count);
    }
}
