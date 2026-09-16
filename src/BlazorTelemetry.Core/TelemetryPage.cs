namespace BlazorTelemetry.Core;

public sealed record TelemetryPage(IReadOnlyList<TelemetryItem> Items, int Total, bool IsTruncated);
