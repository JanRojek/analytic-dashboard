using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.Login;

public sealed class LoginUserHandler
{
    private readonly IUserAccountService _userAccountService;

    public LoginUserHandler(IUserAccountService userAccountService)
    {
        _userAccountService = userAccountService;
    }

    public async Task<UserPasswordSignInResult> HandleAsync(
        LoginUserCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Email)
            || string.IsNullOrWhiteSpace(command.Password))
        {
            return new UserPasswordSignInResult.InvalidCredentials();
        }

        return await _userAccountService.SignInWithPasswordAsync(
            command.Email,
            command.Password,
            command.RememberMe
        );
    }
}
