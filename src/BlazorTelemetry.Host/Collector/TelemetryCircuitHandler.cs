using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using BlazorTelemetry.Core;
using System.Text.Json;

namespace BlazorTelemetry.AspNetCore;

public sealed class TelemetryCircuitHandler(
    NavigationManager navigation,
    IHttpContextAccessor httpContextAccessor,
    IMediator repository,
    IHostEnvironment environment,
    ILogger<TelemetryCircuitHandler> logger) : CircuitHandler, IDisposable
{
    private string? _circuitId;
    private string? _ip;
    private string? _userAgent;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuitId = circuit.Id;
        _ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        _userAgent = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
        navigation.LocationChanged += OnLocationChanged;
        await RecordNavigation(navigation.Uri, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        navigation.LocationChanged -= OnLocationChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = RecordNavigation(args.Location, _lifetime.Token);
    }

    private async Task RecordNavigation(string location, CancellationToken cancellationToken)
    {
        try
        {
            var _uri = new Uri(location);
            var _now = DateTimeOffset.UtcNow;
            await repository.Store([new TelemetryItem
            {
                Kind = TelemetryKind.Request,
                TimestampUtc = _now,
                ObservedUtc = _now,
                ServiceName = environment.ApplicationName,
                Name = _uri.GetLeftPart(UriPartial.Path),
                Body = "Blazor navigation",
                TraceId = Guid.NewGuid().ToString("N"),
                ClientAddress = _ip,
                UserAgent = _userAgent,
                CircuitId = _circuitId,
                Url = _uri.GetLeftPart(UriPartial.Path),
                Source = "Blazor navigation"
            }], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception _exception)
        {
            logger.LogWarning(_exception, "Unable to record Blazor navigation telemetry.");
        }
    }
}
