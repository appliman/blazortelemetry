using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpLogsGrpcService(
    IngestionKeyAuthorizer authorizer,
    OtlpParser parser,
    IngestionCoordinator coordinator,
    IMediator mediator) : LogsService.LogsServiceBase
{
    public override async Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        var authorization = await authorizer.Authorize(
            context.GetHttpContext().Request,
            context.CancellationToken);
        if (!authorization.IsAuthorized)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The ingestion key is invalid."));
        }

        var items = parser.ParseLogs(request, authorization.ApplicationName);
        var result = await coordinator.Enqueue(items, mediator, context.CancellationToken);
        if (!result.Accepted)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "The ingestion queue is full."));
        }
        if (!result.Persisted)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "The telemetry batch could not be persisted."));
        }

        return new ExportLogsServiceResponse();
    }
}
