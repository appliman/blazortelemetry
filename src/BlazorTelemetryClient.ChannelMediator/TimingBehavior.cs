using System.Diagnostics;
using System.Diagnostics.Metrics;
using global::ChannelMediator;

namespace BlazorTelemetry.Client.ChannelMediator;

public sealed class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestType = typeof(TRequest);
        var responseType = typeof(TResponse);
        var tags = new TagList
        {
            { "channelmediator.request.type", requestType.FullName ?? requestType.Name },
            { "channelmediator.response.type", responseType.FullName ?? responseType.Name }
        };
        var startedAt = Stopwatch.GetTimestamp();

        using var activity = ChannelMediatorTelemetry.ActivitySource.StartActivity(
            $"{requestType.Name}Handler",
            ActivityKind.Internal);
        activity?.SetTag("channelmediator.request.type", requestType.FullName ?? requestType.Name);
        activity?.SetTag("channelmediator.response.type", responseType.FullName ?? responseType.Name);
        activity?.SetTag("code.namespace", requestType.Namespace);

        ChannelMediatorTelemetry.ActiveRequests.Add(1, tags);

        try
        {
            var response = await next();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
            activity?.AddException(exception);

            var failureTags = new TagList
            {
                { "channelmediator.request.type", requestType.FullName ?? requestType.Name },
                { "channelmediator.response.type", responseType.FullName ?? responseType.Name },
                { "error.type", exception.GetType().FullName }
            };
            ChannelMediatorTelemetry.Failures.Add(1, failureTags);
            throw;
        }
        finally
        {
            ChannelMediatorTelemetry.Requests.Add(1, tags);
            ChannelMediatorTelemetry.ActiveRequests.Add(-1, tags);
            ChannelMediatorTelemetry.Duration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                tags);
        }
    }
}
