using AnalyticDashboard.Application.Auth.Accounts;

namespace AnalyticDashboard.Application.Auth.Email;

public sealed class EmailConfirmationSender
{
    private readonly IUserAccountTokenService _tokenService;
    private readonly IEmailConfirmationLinkBuilder _linkBuilder;
    private readonly IEmailSender _emailSender;

    public EmailConfirmationSender(
        IUserAccountTokenService tokenService,
        IEmailConfirmationLinkBuilder linkBuilder,
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
        var token = await _tokenService.GenerateEmailConfirmationTokenAsync(
            userId
        );

        var confirmationLink = _linkBuilder.Build(
            userId,
            token
        );

        await _emailSender.SendAsync(
            email,
            "Confirm your email",
            $"<p>Click the link below to confirm your email:</p>" +
            $"<p><a href=\"{confirmationLink}\">Confirm email</a></p>",
            cancellationToken
        );
    }
}
