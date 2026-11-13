using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Application.Auth.ForgotPassword;

public sealed class ForgotPasswordHandler
{
    private readonly IUserAccountService _userAccountService;
    private readonly PasswordResetEmailSender _passwordResetEmailSender;
    private readonly ILogger<ForgotPasswordHandler> _logger;

    public ForgotPasswordHandler(
        IUserAccountService userAccountService,
        PasswordResetEmailSender passwordResetEmailSender,
        ILogger<ForgotPasswordHandler> logger)
    {
        _userAccountService = userAccountService;
        _passwordResetEmailSender = passwordResetEmailSender;
        _logger = logger;
    }

    public async Task HandleAsync(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return;
        }

        var outcome =
            await _userAccountService.FindForPasswordResetAsync(
                command.Email
            );

        if (outcome is not UserPasswordResetLookupResult.Found found)
        {
            return;
        }

        try
        {
            await _passwordResetEmailSender.SendAsync(
                found.Id,
                found.Email,
                cancellationToken
            );
        }
        catch (EmailDeliveryException exception)
        {
            _logger.LogWarning(
                new EventId(1702, "PasswordResetEmailDeliveryFailed"),
                "Email delivery failed during password reset. FailureType: {FailureType}.",
                exception.InnerException?.GetType().Name ?? exception.GetType().Name
            );
        }
    }
}
