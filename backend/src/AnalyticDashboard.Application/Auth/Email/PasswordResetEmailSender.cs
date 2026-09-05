using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.Email;

public sealed class PasswordResetEmailSender
{
    private readonly IUserAccountTokenService _tokenService;
    private readonly IPasswordResetLinkBuilder _linkBuilder;
    private readonly IEmailSender _emailSender;

    public PasswordResetEmailSender(
        IUserAccountTokenService tokenService,
        IPasswordResetLinkBuilder linkBuilder,
        IEmailSender emailSender)
    {
        _tokenService = tokenService;
        _linkBuilder = linkBuilder;
        _emailSender = emailSender;
    }

    public async Task SendAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        var token = await _tokenService.GeneratePasswordResetTokenAsync(
            userId
        );

        var resetLink = _linkBuilder.Build(
            userId,
            token
        );

        await _emailSender.SendAsync(
            email,
            "Reset your password",
            $"<p>Click the link below to reset your password:</p>" +
            $"<p><a href=\"{resetLink}\">Reset password</a></p>",
            cancellationToken
        );
    }
}
