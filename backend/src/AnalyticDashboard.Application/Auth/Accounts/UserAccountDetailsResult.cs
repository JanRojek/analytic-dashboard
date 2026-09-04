namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserAccountDetailsResult
{
    public sealed record Success(
        Guid Id,
        string Email,
        string DisplayName,
        DateTime CreatedAtUtc
    ) : UserAccountDetailsResult;

    public sealed record UserNotFound : UserAccountDetailsResult;
}
