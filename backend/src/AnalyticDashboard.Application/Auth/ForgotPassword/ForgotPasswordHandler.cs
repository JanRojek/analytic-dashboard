using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;

namespace AnalyticDashboard.Application.Auth.ForgotPassword;

public sealed class ForgotPasswordHandler
{
    private readonly IUserAccountService _userAccountService;
    private readonly PasswordResetEmailSender _passwordResetEmailSender;

    public ForgotPasswordHandler(
        IUserAccountService userAccountService,
        PasswordResetEmailSender passwordResetEmailSender)
    {
        _userAccountService = userAccountService;
        _passwordResetEmailSender = passwordResetEmailSender;
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
        catch (EmailDeliveryException) {}
    }
}
