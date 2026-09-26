using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Text;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Contracts.Models.NotificationDeliveries;

namespace BlazorTelemetry.AspNetCore;

internal sealed class NotificationDeliveryService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    BlazorTelemetryOptions options,
    ILogger<NotificationDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            await DispatchPending(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchPending(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var now = DateTimeOffset.UtcNow;
        var pending = await mediator.Send(new GetPendingNotificationDeliveriesRequest(now), cancellationToken);
        if (pending.HasError || pending.Data is null)
        {
            logger.LogError("Failed to load notification deliveries: {Error}", pending.BrokenRules.FirstOrDefault()?.Message);
            return;
        }

        foreach (var delivery in pending.Data)
        {
            string? error = null;
            try
            {
                await Send(delivery, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
                logger.LogWarning(exception, "Notification delivery failed on {Channel} for incident {IncidentId}.", delivery.Channel, delivery.IncidentId);
            }

            var recorded = await mediator.Send(new RecordNotificationDeliveryResultRequest(delivery.Id, error is null, now, error), cancellationToken);
            if (recorded.HasError)
            {
                logger.LogError("Failed to record notification delivery {DeliveryId}: {Error}", delivery.Id, recorded.BrokenRules[0].Message);
            }
        }
    }

    private Task Send(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        return delivery.Channel switch
        {
            "webhook" => SendWebhook(delivery, cancellationToken),
            "ntfy" => SendNtfy(delivery, cancellationToken),
            "email" => SendEmail(delivery, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown channel: {delivery.Channel}")
        };
    }

    private async Task SendWebhook(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Webhook.Url))
        {
            throw new InvalidOperationException("No webhook URL is configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Webhook.Url)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", delivery.EventKey);
        if (!string.IsNullOrWhiteSpace(options.Webhook.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Webhook.BearerToken);
        }
        using var response = await httpClientFactory.CreateClient(nameof(NotificationDeliveryService)).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendNtfy(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Ntfy.Topic))
        {
            throw new InvalidOperationException("No ntfy topic is configured.");
        }

        var url = $"{options.Ntfy.ServerUrl.TrimEnd('/')}/{Uri.EscapeDataString(options.Ntfy.Topic)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("Title", "BlazorTelemetry · alert");
        request.Headers.TryAddWithoutValidation("Tags", "warning,chart_with_upwards_trend");
        if (!string.IsNullOrWhiteSpace(options.Ntfy.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Ntfy.AccessToken);
        }
        using var response = await httpClientFactory.CreateClient(nameof(NotificationDeliveryService)).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendEmail(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Smtp.Host) || string.IsNullOrWhiteSpace(options.Smtp.From) || options.Smtp.Recipients.Length == 0)
        {
            throw new InvalidOperationException("SMTP configuration is incomplete.");
        }

        using var message = new MailMessage { From = new MailAddress(options.Smtp.From), Subject = "BlazorTelemetry · alert", Body = delivery.PayloadJson };
        foreach (var recipient in options.Smtp.Recipients)
        {
            message.To.Add(recipient);
        }
        using var client = new SmtpClient(options.Smtp.Host, options.Smtp.Port)
        {
            EnableSsl = options.Smtp.UseTls,
            Credentials = string.IsNullOrWhiteSpace(options.Smtp.UserName) ? CredentialCache.DefaultNetworkCredentials : new NetworkCredential(options.Smtp.UserName, options.Smtp.Password)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
