using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorTelemetry.Client;

public sealed class ProcessTelemetry(ILogger<ProcessTelemetry> _logger) : IHostedService, IDisposable
{
    public const string METER_NAME = "BlazorTelemetry.Process";
    private readonly Meter _meter = new(METER_NAME);
    private SocketTelemetryListener? _sockets;
    private int _reportedFailure;

    public Task StartAsync(CancellationToken _cancellationToken)
    {
        _sockets = new SocketTelemetryListener();
        _meter.CreateObservableCounter("process.cpu.time", ObserveCpu, "s", "CPU time consumed by this process.");
        _meter.CreateObservableGauge("process.cpu.count", () => Environment.ProcessorCount, "{cpu}", "Processors available to this process.");
        _meter.CreateObservableGauge("process.memory.usage", () => Environment.WorkingSet, "By", "Physical memory used by this process.");
        _meter.CreateObservableCounter("process.network.io", ObserveNetwork, "By", "Bytes transferred by this process through .NET sockets.");
        if (OperatingSystem.IsLinux())
        {
            _meter.CreateObservableCounter("process.disk.io", ObserveDisk, "By", "Bytes transferred to storage by this process.");
        }
        else if (OperatingSystem.IsWindows())
        {
            // Windows exposes all process I/O here, including device and network I/O, not disk alone.
            _meter.CreateObservableCounter("blazortelemetry.process.io", ObserveDisk, "By", "Bytes transferred by this process through file, network and device I/O.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken _cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _sockets?.Dispose();
        _sockets = null;
        _meter.Dispose();
    }

    private IEnumerable<Measurement<double>> ObserveCpu()
    {
        try
        {
            using var _process = Process.GetCurrentProcess();
            return [new(_process.UserProcessorTime.TotalSeconds, new KeyValuePair<string, object?>("cpu.mode", "user")),
                new(_process.PrivilegedProcessorTime.TotalSeconds, new KeyValuePair<string, object?>("cpu.mode", "system"))];
        }
        catch (Exception _exception) when (_exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            ReportFailure(_exception);
            return [];
        }
    }

    private IEnumerable<Measurement<long>> ObserveNetwork()
    {
        var _received = _sockets?.Received ?? -1;
        var _sent = _sockets?.Sent ?? -1;
        if (_received >= 0)
        {
            yield return new(_received, new KeyValuePair<string, object?>("network.io.direction", "receive"));
        }
        if (_sent >= 0)
        {
            yield return new(_sent, new KeyValuePair<string, object?>("network.io.direction", "transmit"));
        }
    }

    private IEnumerable<Measurement<long>> ObserveDisk()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var _values = File.ReadLines("/proc/self/io").Select(_line => _line.Split(':', 2))
                    .Where(_parts => _parts.Length == 2).ToDictionary(_parts => _parts[0], _parts => _parts[1].Trim());
                return [new(long.Parse(_values["read_bytes"], CultureInfo.InvariantCulture), new KeyValuePair<string, object?>("disk.io.direction", "read")),
                    new(long.Parse(_values["write_bytes"], CultureInfo.InvariantCulture), new KeyValuePair<string, object?>("disk.io.direction", "write"))];
            }
            if (OperatingSystem.IsWindows())
            {
                using var _process = Process.GetCurrentProcess();
                if (!GetProcessIoCounters(_process.Handle, out var _counters))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return [new(checked((long)_counters.ReadTransferCount), new KeyValuePair<string, object?>("disk.io.direction", "read")),
                    new(checked((long)_counters.WriteTransferCount), new KeyValuePair<string, object?>("disk.io.direction", "write"))];
            }
        }
        catch (Exception _exception) when (_exception is IOException or UnauthorizedAccessException or Win32Exception or FormatException or KeyNotFoundException or OverflowException)
        {
            ReportFailure(_exception);
        }
        return [];
    }

    private void ReportFailure(Exception _exception)
    {
        if (Interlocked.Exchange(ref _reportedFailure, 1) == 0)
        {
            _logger.LogWarning(_exception, "Some process metrics could not be read on this host.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr _process, out ProcessIoCounters _counters);
}
