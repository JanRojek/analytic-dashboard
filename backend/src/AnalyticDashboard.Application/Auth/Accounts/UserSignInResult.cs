namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserSignInResult
{
    public sealed record Success
        : UserSignInResult;

    public sealed record EmailNotConfirmed
        : UserSignInResult;

    public sealed record UserNotFound
        : UserSignInResult;
}
