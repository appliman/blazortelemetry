using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using ChannelMediator;
using ChannelMediator.InMemory;
using BlazorTelemetry.Host.ModelExtensions;
using BlazorTelemetry.Host.Contracts.Models.Maintenance;
using BlazorTelemetry.Host.Validators;
using FluentValidation;

namespace BlazorTelemetry.Tests;

public sealed partial class TelemetryCqrsTests
{
    [Fact]
    public async Task AlertEvaluationFiresOnlyForApplicationWithMoreThanFiveErrors()
    {
        var factory = new TestDbContextFactory(_options);
        var repository = _mediator;
        var rule = new AlertRule
        {
            Name = "Application error burst",
            ServiceName = "*",
            Type = AlertRuleType.ErrorLogs,
            Threshold = 5,
            WindowMinutes = 1,
            ConfirmationMinutes = 0,
            NotifyWebhook = true
        };
        await repository.SaveAlertRule(rule, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var apiErrors = Enumerable.Range(0, 6).Select(index => new TelemetryItem
        {
            Kind = TelemetryKind.Log,
            TimestampUtc = now.AddSeconds(-index),
            ObservedUtc = now,
            ServiceName = "api",
            Name = $"api-error-{index}",
            SeverityNumber = 17
        });
        var workerErrors = Enumerable.Range(0, 5).Select(index => new TelemetryItem
        {
            Kind = TelemetryKind.Log,
            TimestampUtc = now.AddSeconds(-index),
            ObservedUtc = now,
            ServiceName = "worker",
            Name = $"worker-error-{index}",
            SeverityNumber = 17
        });
        await repository.Store(apiErrors.Concat(workerErrors).ToArray(), CancellationToken.None);

        var evaluationService = new AlertEvaluationService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new BlazorTelemetryOptions(),
            NullLogger<AlertEvaluationService>.Instance);

        await evaluationService.Evaluate(CancellationToken.None);

        await using var context = new TelemetryDbContext(_options);
        var incident = await context.Incidents.SingleAsync();
        Assert.Equal("api", incident.ServiceName);
        Assert.Equal(6, incident.ObservedValue);
        Assert.Equal(IncidentState.Firing, incident.State);
        var delivery = await context.NotificationDeliveries.SingleAsync();
        Assert.Equal(incident.Id, delivery.IncidentId);
        Assert.Equal($"incident:{incident.Id}:firing:webhook", delivery.EventKey);
    }

}
