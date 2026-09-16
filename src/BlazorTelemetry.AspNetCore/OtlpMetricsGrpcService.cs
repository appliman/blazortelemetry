using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpMetricsGrpcService(
    IngestionKeyAuthorizer authorizer,
    OtlpParser parser,
    TelemetryIngestionQueue queue) : MetricsService.MetricsServiceBase
{
    public override async Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context)
    {
        var authorization = await authorizer.Authorize(
            context.GetHttpContext().Request,
            context.CancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The ingestion key is invalid."));
        }

        var items = parser.ParseMetrics(request, authorization.ApplicationName);
        if (!await queue.Enqueue(items, context.CancellationToken))
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "The ingestion queue is full."));
        }

        return new ExportMetricsServiceResponse();
    }
}
