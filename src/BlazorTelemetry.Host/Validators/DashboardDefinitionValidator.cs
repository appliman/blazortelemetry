using BlazorTelemetry.Core;
using FluentValidation;

namespace BlazorTelemetry.Host.Validators;

public sealed class DashboardDefinitionValidator : AbstractValidator<DashboardDefinition>
{
    public DashboardDefinitionValidator()
    {
        RuleFor(dashboard => dashboard.Name).NotEmpty().MaximumLength(200);
        RuleFor(dashboard => dashboard.DefinitionJson).NotEmpty();
    }
}
