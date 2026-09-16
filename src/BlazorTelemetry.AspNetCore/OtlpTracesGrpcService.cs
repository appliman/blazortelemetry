using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpTracesGrpcService(
    IngestionKeyAuthorizer authorizer,
    OtlpParser parser,
    TelemetryIngestionQueue queue) : TraceService.TraceServiceBase
{
    public override async Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context)
    {
        var authorization = await authorizer.Authorize(
            context.GetHttpContext().Request,
            context.CancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The ingestion key is invalid."));
        }

        var items = parser.ParseTraces(request, authorization.ApplicationName);
        if (!await queue.Enqueue(items, context.CancellationToken))
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "The ingestion queue is full."));
        }

        return new ExportTraceServiceResponse();
    }
}
