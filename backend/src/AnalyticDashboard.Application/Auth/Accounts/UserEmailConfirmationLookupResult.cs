namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserEmailConfirmationLookupResult
{
    public sealed record Unconfirmed(
        Guid Id,
        string Email
    ) : UserEmailConfirmationLookupResult;

    public sealed record AlreadyConfirmed
        : UserEmailConfirmationLookupResult;

    public sealed record UserNotFound
        : UserEmailConfirmationLookupResult;
}
