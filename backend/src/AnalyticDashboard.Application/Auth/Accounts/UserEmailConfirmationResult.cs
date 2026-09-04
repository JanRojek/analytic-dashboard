namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserEmailConfirmationResult
{
    public sealed record Success
        : UserEmailConfirmationResult;

    public sealed record UserNotFound
        : UserEmailConfirmationResult;

    public sealed record InvalidToken
        : UserEmailConfirmationResult;

    public sealed record AlreadyConfirmed
        : UserEmailConfirmationResult;
}
