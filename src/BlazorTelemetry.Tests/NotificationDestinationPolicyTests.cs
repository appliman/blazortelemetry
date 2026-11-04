using System.Net;
using System.Net.Sockets;
using BlazorTelemetry.Host.Configuration;

namespace BlazorTelemetry.Tests;

public sealed class NotificationDestinationPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.1.2")]
    [InlineData("192.168.1.1")]
    [InlineData("100.100.100.200")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:7f00:1::1")]
    public void InternalAndMetadataAddressesAreDenied(string address)
    {
        Assert.False(NotificationDestinationPolicy.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void PublicAddressesAreAllowed(string address)
    {
        Assert.True(NotificationDestinationPolicy.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task LoopbackConnectionRequiresExplicitHostPermission()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var denied = new HttpClient(new NotificationDestinationPolicy([]).CreateHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => denied.GetAsync($"http://127.0.0.1:{endpoint.Port}/"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = socket.GetStream();
            var buffer = new byte[4096];
            var received = await stream.ReadAsync(buffer, timeout.Token);
            Assert.True(received > 0);
            await stream.WriteAsync("HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        using var allowed = new HttpClient(new NotificationDestinationPolicy(["127.0.0.1"]).CreateHandler());
        using var result = await allowed.GetAsync($"http://127.0.0.1:{endpoint.Port}/", timeout.Token);
        Assert.Equal(HttpStatusCode.Found, result.StatusCode);
        await response;
    }
}
