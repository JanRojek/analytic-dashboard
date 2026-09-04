namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserEmailConfirmationStatusResult
{
    public sealed record Confirmed
        : UserEmailConfirmationStatusResult;

    public sealed record NotConfirmed
        : UserEmailConfirmationStatusResult;

    public sealed record UserNotFound
        : UserEmailConfirmationStatusResult;
}
