namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserPasswordResetResult
{
    public sealed record Success
        : UserPasswordResetResult;

    public sealed record UserNotFound
        : UserPasswordResetResult;

    public sealed record InvalidToken
        : UserPasswordResetResult;

    public sealed record InvalidPassword(
        IReadOnlyList<string> Messages
    ) : UserPasswordResetResult;
}
