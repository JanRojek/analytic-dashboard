using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.ResetPassword;

public sealed class ResetPasswordHandler
{
    private readonly IUserAccountTokenService _tokenService;

    public ResetPasswordHandler(
        IUserAccountTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async Task<UserPasswordResetResult> HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        return await _tokenService.ResetPasswordAsync(
            command.UserId,
            command.Token,
            command.NewPassword,
            cancellationToken
        );
    }
}
