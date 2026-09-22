using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using BlazorTelemetry.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorTelemetry.Tests;

public sealed class ProcessTelemetryTests
{
    [Fact]
    public async Task CollectsActualProcessMemoryCpuSocketTrafficAndIo()
    {
        var _values = new ConcurrentDictionary<string, double>();
        using var _listener = new MeterListener
        {
            InstrumentPublished = (_instrument, _meterListener) =>
            {
                if (_instrument.Meter.Name == ProcessTelemetry.METER_NAME)
                {
                    _meterListener.EnableMeasurementEvents(_instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<double>((_instrument, _value, _tags, _) => _values[_instrument.Name] = _value);
        _listener.SetMeasurementEventCallback<int>((_instrument, _value, _tags, _) => _values[_instrument.Name] = _value);
        _listener.SetMeasurementEventCallback<long>((_instrument, _value, _tags, _) =>
        {
            var _direction = _tags.ToArray().FirstOrDefault(_tag => _tag.Key.EndsWith(".direction", StringComparison.Ordinal)).Value;
            _values[_instrument.Name + (_direction is null ? string.Empty : ":" + _direction)] = _value;
        });
        _listener.Start();
        using var _telemetry = new ProcessTelemetry(NullLogger<ProcessTelemetry>.Instance);
        await _telemetry.StartAsync(CancellationToken.None);

        using var _timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        using var _client = new TcpClient();
        var _accept = _server.AcceptTcpClientAsync(_timeout.Token);
        await _client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_server.LocalEndpoint).Port, _timeout.Token);
        using var _connection = await _accept;
        var _bytes = new byte[8192];
        await _client.GetStream().WriteAsync(_bytes, _timeout.Token);
        await _connection.GetStream().ReadExactlyAsync(_bytes, _timeout.Token);
        using var _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        do
        {
            _listener.RecordObservableInstruments();
            if (_values.GetValueOrDefault("process.network.io:receive") >= _bytes.Length && _values.GetValueOrDefault("process.network.io:transmit") >= _bytes.Length)
            {
                break;
            }
        }
        while (await _timer.WaitForNextTickAsync(_timeout.Token));

        Assert.True(_values["process.memory.usage"] > 0);
        Assert.True(_values["process.cpu.count"] >= 1);
        Assert.True(_values["process.cpu.time"] >= 0);
        Assert.True(_values["process.network.io:receive"] >= _bytes.Length);
        Assert.True(_values["process.network.io:transmit"] >= _bytes.Length);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(_values["blazortelemetry.process.io:read"] >= 0);
            Assert.True(_values["blazortelemetry.process.io:write"] >= 0);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.True(_values["process.disk.io:read"] >= 0);
            Assert.True(_values["process.disk.io:write"] >= 0);
        }
        await _telemetry.StopAsync(CancellationToken.None);
    }
}
