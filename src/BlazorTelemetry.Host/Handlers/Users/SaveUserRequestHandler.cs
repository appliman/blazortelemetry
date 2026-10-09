using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using BlazorTelemetry.Host.Authentication;
using BlazorTelemetry.Host.Contracts.Models.Users;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Host.Handlers.Users;

public sealed class SaveUserRequestHandler(IDbContextFactory<TelemetryDbContext> factory, UserAuthenticationService authentication,
    IValidator<TelemetryUser> validator, ILogger<SaveUserRequestHandler> logger, IMediator? mediator = null) : IRequestHandler<SaveUserRequest, QueryResult<string>>
{
    public async Task<QueryResult<string>> Handle(SaveUserRequest request, CancellationToken cancellationToken)
    {
        if (!authentication.IsAdministrator(request.Principal))
        {
            return new() { BrokenRules = [new("forbidden", "", "Administrator access is required.")] };
        }
        try
        {
            request.User.Identifier = request.User.Identifier.Trim();
            var _validation = await validator.ValidateAsync(request.User, cancellationToken);
            if (!_validation.IsValid)
            {
                return new() { BrokenRules = _validation.Errors.Select(error => new BrokenRule("validation", error.PropertyName, error.ErrorMessage)).ToArray() };
            }
            await using var _context = await factory.CreateDbContextAsync(cancellationToken);
            if (await _context.Users.AnyAsync(_user => _user.Id != request.User.Id && _user.Identifier == request.User.Identifier, cancellationToken))
            {
                return new() { BrokenRules = [new("duplicate", "Identifier", "This identifier is already in use.")] };
            }
            var _user = request.User.Id == 0 ? new TelemetryUser() : await _context.Users.AsTracking().SingleOrDefaultAsync(_user => _user.Id == request.User.Id, cancellationToken);
            if (_user is null || (_user.Id != 0 && _user.SecurityVersion != request.ExpectedVersion))
            {
                return new() { BrokenRules = [new("conflict", "", "The user changed. Reload the page and try again.")] };
            }
            var _isNew = _user.Id == 0;
            var _reset = _isNew || request.ResetSecret || _user.Identifier != request.User.Identifier;
            string? _secret = null;
            if (_reset)
            {
                var _guid = Guid.NewGuid();
                _secret = _guid.ToString("D");
                _user.ProtectedSecret = authentication.Protect(_guid);
                _user.ActivatedUtc = null;
                _user.LastAcceptedInterval = -1;
            }
            if (_reset || _user.IsActive != request.User.IsActive)
            {
                _user.SecurityVersion = Guid.NewGuid();
            }
            _user.Identifier = request.User.Identifier;
            _user.IsActive = request.User.IsActive;
            if (_isNew)
            {
                _context.Users.Add(_user);
            }
            else
            {
                _context.Entry(_user).Property(value => value.SecurityVersion).OriginalValue = request.ExpectedVersion;
            }
            await _context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("User {UserId} saved. New account: {IsNewAccount}, secret reset: {SecretReset}, active: {IsActive}.", _user.Id, _isNew, _reset, _user.IsActive);
            if (mediator is not null)
            {
                try
                {
                    await mediator.Publish(new UserChangedNotification(_user.Id, _isNew), cancellationToken);
                }
                catch (Exception _exception)
                {
                    logger.LogError(_exception, "Publishing the user change notification failed.");
                }
            }
            request.User.Id = _user.Id;
            request.User.SecurityVersion = _user.SecurityVersion;
            request.User.ActivatedUtc = _user.ActivatedUtc;
            return new() { Data = _secret, ChangeCount = 1 };
        }
        catch (DbUpdateException)
        {
            return new() { BrokenRules = [new("conflict", "", "The user changed or the identifier is already in use. Reload and try again.")] };
        }
        catch (Exception _exception)
        {
            logger.LogError(_exception, "Saving a user failed.");
            return new() { BrokenRules = [new("storage_error", "", "The operation failed.")] };
        }
    }
}
