using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Authentication;
using FluentValidation;

namespace BlazorTelemetry.Host.Validators;

public sealed class TelemetryUserValidator : AbstractValidator<TelemetryUser>
{
    public TelemetryUserValidator(BlazorAuthConfiguration settings)
    {
        RuleFor(user => user.Identifier).NotEmpty().MaximumLength(256)
            .Must(identifier => !settings.AllowedUserLogins.Contains(identifier, StringComparer.OrdinalIgnoreCase))
            .WithMessage("This identifier is reserved for a configured administrator.");
    }
}
