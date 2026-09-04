using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.CurrentUser;

public sealed class GetCurrentUserHandler
{
    private readonly IUserAccountService _userAccountService;

    public GetCurrentUserHandler(
        IUserAccountService userAccountService)
    {
        _userAccountService = userAccountService;
    }

    public async Task<UserAccountDetailsResult> HandleAsync(
        GetCurrentUserQuery query)
    {
        return await _userAccountService.GetByIdAsync(
            query.UserId
        );
    }
}
