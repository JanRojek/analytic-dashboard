using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;

namespace AnalyticDashboard.Application.Auth.ResendConfirmation;

public sealed class ResendConfirmationHandler
{
    private readonly IUserAccountService _userAccountService;
    private readonly EmailConfirmationSender _emailConfirmationSender;

    public ResendConfirmationHandler(
        IUserAccountService userAccountService,
        EmailConfirmationSender emailConfirmationSender)
    {
        _userAccountService = userAccountService;
        _emailConfirmationSender = emailConfirmationSender;
    }

    public async Task HandleAsync(
        ResendConfirmationCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return;
        }

        var result = await _userAccountService.FindForConfirmationAsync(
            command.Email
        );

        if (result is UserEmailConfirmationLookupResult.Unconfirmed unconfirmed)
        {
            try
            {
                await _emailConfirmationSender.SendAsync(
                    unconfirmed.Id,
                    unconfirmed.Email,
                    cancellationToken
                );
            }
            catch (EmailDeliveryException) {}
        }
    }
}
