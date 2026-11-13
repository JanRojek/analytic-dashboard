using System.Diagnostics;
using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Application.Auth.Register;

public sealed class RegisterUserHandler
{
    private readonly IUserAccountService _userAccountService;
    private readonly EmailConfirmationSender _emailConfirmationSender;
    private readonly ILogger<RegisterUserHandler> _logger;

    public RegisterUserHandler(
        IUserAccountService userAccountService,
        EmailConfirmationSender emailSender,
        ILogger<RegisterUserHandler> logger)
    {
        _userAccountService = userAccountService;
        _emailConfirmationSender = emailSender;
        _logger = logger;
    }

    public async Task<RegisterUserResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return new RegisterUserResult.InvalidDisplayName(
                "DisplayName cannot be empty."
            );
        }

        var displayName = command.DisplayName.Trim();

        if (displayName.EnumerateRunes().Count() >
            UserAccountRules.MaxDisplayNameLength)
        {
            return new RegisterUserResult.InvalidDisplayName(
                "DisplayName cannot be longer than " +
                $"{UserAccountRules.MaxDisplayNameLength} characters."
            );
        }

        if (string.IsNullOrWhiteSpace(command.Password))
        {
            return new RegisterUserResult.InvalidPassword(
                ["Password is required."]
            );
        }

        var outcome = await _userAccountService.CreateAsync(
            command.Email,
            displayName,
            command.Password
        );

        if (outcome is not UserAccountCreationResult.Success success)
        {
            return outcome switch
            {
                UserAccountCreationResult.EmailAlreadyExists exists =>
                    new RegisterUserResult.EmailAlreadyExists(
                        exists.ConflictingEmail
                    ),

                UserAccountCreationResult.InvalidEmail invalid =>
                    new RegisterUserResult.InvalidEmail(
                        invalid.Message
                    ),

                UserAccountCreationResult.InvalidPassword invalid =>
                    new RegisterUserResult.InvalidPassword(
                        invalid.Messages
                    ),

                _ => throw new UnreachableException()
            };
        }

        try
        {
            await _emailConfirmationSender.SendAsync(
                success.Id,
                command.Email,
                cancellationToken
            );
        }
        catch (EmailDeliveryException exception)
        {
            _logger.LogWarning(
                new EventId(1701, "RegistrationEmailDeliveryFailed"),
                "Email delivery failed during registration. FailureType: {FailureType}.",
                exception.InnerException?.GetType().Name ?? exception.GetType().Name
            );
        }

        return new RegisterUserResult.Success(
            success.Id,
            command.Email,
            displayName,
            success.CreatedAtUtc
        );
    }
}
