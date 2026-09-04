using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.CompleteRegistration;

public sealed class CompleteRegistrationHandler
{
    private readonly IUserAccountService _userAccountService;

    public CompleteRegistrationHandler(IUserAccountService userAccountService)
    {
        _userAccountService = userAccountService;
    }

    public async Task<UserSignInResult> HandleAsync(
        CompleteRegistrationCommand command)
    {
        return await _userAccountService.SignInAsync(
            command.UserId
        );
    }
}
