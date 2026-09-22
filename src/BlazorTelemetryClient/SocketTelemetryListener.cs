using System.Diagnostics.Tracing;
using System.Globalization;

namespace BlazorTelemetry.Client;

internal sealed class SocketTelemetryListener : EventListener
{
    private long _received = -1;
    private long _sent = -1;

    public long Received => Interlocked.Read(ref _received);
    public long Sent => Interlocked.Read(ref _sent);

    protected override void OnEventSourceCreated(EventSource _eventSource)
    {
        if (_eventSource.Name == "System.Net.Sockets")
        {
            EnableEvents(_eventSource, EventLevel.LogAlways, EventKeywords.All,
                new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs _eventData)
    {
        if (_eventData.EventName != "EventCounters" || _eventData.Payload?.FirstOrDefault() is not IDictionary<string, object> _counter
            || !_counter.TryGetValue("Name", out var _name) || !_counter.TryGetValue("Mean", out var _mean))
        {
            return;
        }
        if (_name is "bytes-received")
        {
            Interlocked.Exchange(ref _received, Convert.ToInt64(_mean, CultureInfo.InvariantCulture));
        }
        else if (_name is "bytes-sent")
        {
            Interlocked.Exchange(ref _sent, Convert.ToInt64(_mean, CultureInfo.InvariantCulture));
        }
    }
}
