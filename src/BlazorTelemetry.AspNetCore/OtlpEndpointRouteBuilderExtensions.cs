using Google.Protobuf;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace BlazorTelemetry.AspNetCore;

public static class OtlpEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapBlazorTelemetry(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/logs", ReceiveLogs).DisableAntiforgery();
        endpoints.MapPost("/v1/traces", ReceiveTraces).DisableAntiforgery();
        endpoints.MapPost("/v1/metrics", ReceiveMetrics).DisableAntiforgery();
        endpoints.MapGrpcService<OtlpLogsGrpcService>();
        endpoints.MapGrpcService<OtlpTracesGrpcService>();
        endpoints.MapGrpcService<OtlpMetricsGrpcService>();
        endpoints.MapGet("/blazor-telemetry/health", GetHealth).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ReceiveLogs(
        HttpRequest request,
        IngestionKeyAuthorizer authorizer,
        OtlpParser parser,
        TelemetryIngestionQueue queue,
        ILogger<OtlpParser> logger,
        BlazorTelemetry.Core.BlazorTelemetryOptions options,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizer.Authorize(request, cancellationToken);
        if (!authorization.IsAuthorized)
        {
            return TypedResults.Unauthorized();
        }

        var payload = await ReadBody(request, options.MaximumRequestBytes, cancellationToken);
        if (payload is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var items = parser.ParseLogs(ExportLogsServiceRequest.Parser.ParseFrom(payload), authorization.ApplicationName);
            return await Persist(queue, items, new ExportLogsServiceResponse(), cancellationToken);
        }
        catch (InvalidProtocolBufferException exception)
        {
            logger.LogWarning(exception, "Invalid OTLP logs payload.");
            return TypedResults.BadRequest($"Invalid OTLP logs payload: {exception.Message}");
        }
    }

    private static async Task<IResult> ReceiveTraces(
        HttpRequest request,
        IngestionKeyAuthorizer authorizer,
        OtlpParser parser,
        TelemetryIngestionQueue queue,
        ILogger<OtlpParser> logger,
        BlazorTelemetry.Core.BlazorTelemetryOptions options,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizer.Authorize(request, cancellationToken);
        if (!authorization.IsAuthorized)
        {
            return TypedResults.Unauthorized();
        }

        var payload = await ReadBody(request, options.MaximumRequestBytes, cancellationToken);
        if (payload is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var items = parser.ParseTraces(ExportTraceServiceRequest.Parser.ParseFrom(payload), authorization.ApplicationName);
            return await Persist(queue, items, new ExportTraceServiceResponse(), cancellationToken);
        }
        catch (InvalidProtocolBufferException exception)
        {
            logger.LogWarning(exception, "Invalid OTLP traces payload.");
            return TypedResults.BadRequest($"Invalid OTLP traces payload: {exception.Message}");
        }
    }

    private static async Task<IResult> ReceiveMetrics(
        HttpRequest request,
        IngestionKeyAuthorizer authorizer,
        OtlpParser parser,
        TelemetryIngestionQueue queue,
        ILogger<OtlpParser> logger,
        BlazorTelemetry.Core.BlazorTelemetryOptions options,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizer.Authorize(request, cancellationToken);
        if (!authorization.IsAuthorized)
        {
            return TypedResults.Unauthorized();
        }

        var payload = await ReadBody(request, options.MaximumRequestBytes, cancellationToken);
        if (payload is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var items = parser.ParseMetrics(ExportMetricsServiceRequest.Parser.ParseFrom(payload), authorization.ApplicationName);
            return await Persist(queue, items, new ExportMetricsServiceResponse(), cancellationToken);
        }
        catch (InvalidProtocolBufferException exception)
        {
            logger.LogWarning(exception, "Invalid OTLP metrics payload.");
            return TypedResults.BadRequest($"Invalid OTLP metrics payload: {exception.Message}");
        }
    }

    private static IResult GetHealth(CollectorCounters counters, BlazorTelemetry.Core.BlazorTelemetryOptions options)
    {
        var databasePath = GetDatabasePath(options.ConnectionString);
        var databaseBytes = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0;
        var walPath = $"{databasePath}-wal";
        var walBytes = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;
        var root = Path.GetPathRoot(Path.GetFullPath(databasePath)) ?? Path.GetPathRoot(Environment.CurrentDirectory)!;
        var available = new DriveInfo(root).AvailableFreeSpace;
        var gcInfo = GC.GetGCMemoryInfo();
        return TypedResults.Ok(new BlazorTelemetry.Core.CollectorHealth(
            counters.Received,
            counters.Persisted,
            counters.Rejected,
            counters.Purged,
            counters.QueueDepth,
            databaseBytes,
            walBytes,
            available)
        {
            ServerGarbageCollection = System.Runtime.GCSettings.IsServerGC,
            ProcessWorkingSetBytes = Environment.WorkingSet,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            ManagedHeapCommittedBytes = gcInfo.TotalCommittedBytes,
            ManagedHeapFragmentedBytes = gcInfo.FragmentedBytes,
            ManagedTotalAllocatedBytes = GC.GetTotalAllocatedBytes(false),
            GcMemoryLoadBytes = gcInfo.MemoryLoadBytes,
            GcTotalAvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes
        });
    }

    private static async Task<IResult> Persist(TelemetryIngestionQueue queue, IReadOnlyList<BlazorTelemetry.Core.TelemetryItem> items, IMessage response, CancellationToken cancellationToken)
    {
        if (!await queue.Enqueue(items, cancellationToken))
        {
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Bytes(response.ToByteArray(), "application/x-protobuf");
    }

    private static async Task<byte[]?> ReadBody(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            return null;
        }

        await using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static string GetDatabasePath(string connectionString)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        return Path.IsPathRooted(builder.DataSource)
            ? builder.DataSource
            : Path.Combine(Environment.CurrentDirectory, builder.DataSource);
    }
}
