using System.Diagnostics;
using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;

namespace AnalyticDashboard.Application.Auth.Register;

public sealed class RegisterUserHandler
{
    private readonly IUserAccountService _userAccountService;
    private readonly EmailConfirmationSender _emailConfirmationSender;

    public RegisterUserHandler(
        IUserAccountService userAccountService,
        EmailConfirmationSender emailSender)
    {
        _userAccountService = userAccountService;
        _emailConfirmationSender = emailSender;
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
                command.Email
            );
        }
        catch (EmailDeliveryException) {}

        return new RegisterUserResult.Success(
            success.Id,
            command.Email,
            displayName,
            success.CreatedAtUtc
        );
    }
}
