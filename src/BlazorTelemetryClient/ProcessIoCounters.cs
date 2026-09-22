using System.Runtime.InteropServices;

namespace BlazorTelemetry.Client;

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessIoCounters
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}
