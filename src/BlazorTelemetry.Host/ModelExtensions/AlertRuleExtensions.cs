using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.AlertRules;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class AlertRuleExtensions
{
    public static async Task<IReadOnlyList<AlertRule>> GetAlertRules(this IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAlertRulesRequest(), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task SaveAlertRule(this IMediator mediator, AlertRule rule, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SaveAlertRuleRequest(rule), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

    public static async Task DeleteAlertRule(this IMediator mediator, int id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteAlertRuleRequest(id), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

}
