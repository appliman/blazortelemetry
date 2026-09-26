namespace BlazorTelemetry.Core;

public static class ResourceMetricNames
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "process.cpu.utilization", "process.cpu.time", "dotnet.process.cpu.time", "process.cpu.count", "dotnet.process.cpu.count",
        "process.memory.usage", "dotnet.process.memory.working_set", "dotnet.gc.last_collection.memory.committed_size",
        "process.runtime.dotnet.gc.committed_memory.size", "process.runtime.dotnet.gc.heap.size",
        "process.network.io", "process.disk.io", "blazortelemetry.process.io",
        "aspnetcore.components.circuit.active", "aspnetcore.components.circuit.connected", "http.server.active_requests"
    });
}
