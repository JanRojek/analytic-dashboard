namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserPasswordResetLookupResult
{
    public sealed record Found(
        Guid Id,
        string Email
    ) : UserPasswordResetLookupResult;

    public sealed record UserNotFound : UserPasswordResetLookupResult;
}
