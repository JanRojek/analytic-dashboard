namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserPasswordSignInResult
{
    public sealed record Success
        : UserPasswordSignInResult;

    public sealed record InvalidCredentials
        : UserPasswordSignInResult;

    public sealed record EmailNotConfirmed
        : UserPasswordSignInResult;
}
