namespace AnalyticDashboard.Application.Auth.Accounts;

public abstract record UserAccountCreationResult
{
    public sealed record Success(
        Guid Id,
        DateTime CreatedAtUtc
    ) : UserAccountCreationResult;

    public sealed record EmailAlreadyExists(
        string ConflictingEmail
    ) : UserAccountCreationResult;

    public sealed record InvalidEmail(
        string Message
    ) : UserAccountCreationResult;

    public sealed record InvalidPassword(
        IReadOnlyList<string> Messages
    ) : UserAccountCreationResult;
}
