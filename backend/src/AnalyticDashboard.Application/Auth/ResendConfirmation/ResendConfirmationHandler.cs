using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Application.Auth.ResendConfirmation;

public sealed class ResendConfirmationHandler
{
    private readonly IUserAccountService _userAccountService;
    private readonly EmailConfirmationSender _emailConfirmationSender;
    private readonly ILogger<ResendConfirmationHandler> _logger;

    public ResendConfirmationHandler(
        IUserAccountService userAccountService,
        EmailConfirmationSender emailConfirmationSender,
        ILogger<ResendConfirmationHandler> logger)
    {
        _userAccountService = userAccountService;
        _emailConfirmationSender = emailConfirmationSender;
        _logger = logger;
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
            catch (EmailDeliveryException exception)
            {
                _logger.LogWarning(
                    new EventId(1703, "ConfirmationEmailDeliveryFailed"),
                    "Email delivery failed while resending confirmation. FailureType: {FailureType}.",
                    exception.InnerException?.GetType().Name ?? exception.GetType().Name
                );
            }
        }
    }
}
