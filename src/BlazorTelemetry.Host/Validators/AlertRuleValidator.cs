using BlazorTelemetry.Core;
using FluentValidation;

namespace BlazorTelemetry.Host.Validators;

public sealed class AlertRuleValidator : AbstractValidator<AlertRule>
{
    public AlertRuleValidator()
    {
        RuleFor(rule => rule.Name).NotEmpty().MaximumLength(200);
        RuleFor(rule => rule.ServiceName).NotEmpty().MaximumLength(256);
        RuleFor(rule => rule.WindowMinutes).GreaterThan(0);
        RuleFor(rule => rule.ConfirmationMinutes).GreaterThanOrEqualTo(0);
        RuleFor(rule => rule.Threshold).GreaterThanOrEqualTo(0);
    }
}
