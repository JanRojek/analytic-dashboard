using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.Logout;

public sealed class LogoutUserHandler
{
    private readonly IUserAccountService _userAccountService;

    public LogoutUserHandler(IUserAccountService userAccountService)
    {
        _userAccountService = userAccountService;
    }

    public async Task HandleAsync()
    {
        await _userAccountService.SignOutAsync();
    }
}
