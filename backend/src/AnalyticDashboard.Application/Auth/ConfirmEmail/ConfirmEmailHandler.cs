using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.ConfirmEmail;

public sealed class ConfirmEmailHandler
{
    private readonly IUserAccountTokenService _tokenService;

    public ConfirmEmailHandler(
        IUserAccountTokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async Task<UserEmailConfirmationResult> HandleAsync(
        ConfirmEmailCommand command,
        CancellationToken cancellationToken)
    {
        return await _tokenService.ConfirmEmailAsync(
            command.UserId,
            command.Token,
            cancellationToken
        );
    }
}
