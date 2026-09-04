namespace AnalyticDashboard.Application.Auth.Accounts;

public interface IUserAccountTokenService
{
    Task<string> GenerateEmailConfirmationTokenAsync(
        Guid userId
    );

    Task<string> GeneratePasswordResetTokenAsync(
        Guid userId
    );

    Task<UserEmailConfirmationResult> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken
    );

    Task<UserPasswordResetResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword
    );
}
