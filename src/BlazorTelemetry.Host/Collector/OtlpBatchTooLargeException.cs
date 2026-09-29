namespace BlazorTelemetry.AspNetCore;

internal sealed class OtlpBatchTooLargeException(int maximumItems)
    : InvalidOperationException($"The OTLP batch contains more than {maximumItems:N0} telemetry items.");
