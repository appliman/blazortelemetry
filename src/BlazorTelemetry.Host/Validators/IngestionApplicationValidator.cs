using BlazorTelemetry.Core;
using FluentValidation;

namespace BlazorTelemetry.Host.Validators;

public sealed class IngestionApplicationValidator : AbstractValidator<IngestionApplication>
{
    public IngestionApplicationValidator()
    {
        RuleFor(application => application.Name).NotEmpty().MaximumLength(100);
        RuleFor(application => application.KeyHash).NotEmpty();
        RuleFor(application => application.KeyPrefix).NotEmpty();
    }
}
