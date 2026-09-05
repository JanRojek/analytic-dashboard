using System.Diagnostics;
using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.RegistrationStatus;

public sealed class GetRegistrationStatusHandler
{
    private readonly IUserAccountService _accountService;

    public GetRegistrationStatusHandler(IUserAccountService accountService)
    {
        _accountService = accountService;
    }

    public async Task<GetRegistrationStatusResult> HandleAsync(
        GetRegistrationStatusQuery query)
    {
        var outcome =
            await _accountService.GetEmailConfirmationStatusAsync(
                query.UserId
            );

        return outcome switch
        {
            UserEmailConfirmationStatusResult.Confirmed =>
                new GetRegistrationStatusResult.Confirmed(),

            UserEmailConfirmationStatusResult.NotConfirmed =>
                new GetRegistrationStatusResult.Pending(),

            UserEmailConfirmationStatusResult.UserNotFound =>
                new GetRegistrationStatusResult.UserNotFound(),

            _ => throw new UnreachableException()
        };
    }
}
