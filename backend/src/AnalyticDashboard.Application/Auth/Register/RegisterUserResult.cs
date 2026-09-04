namespace AnalyticDashboard.Application.Auth.Register;

public abstract record RegisterUserResult
{
    public sealed record Success(
        Guid Id,
        string Email,
        string DisplayName,
        DateTime CreatedAtUtc
    ) : RegisterUserResult;

    public sealed record InvalidDisplayName(
        string Message
    ) : RegisterUserResult;

    public sealed record InvalidEmail(
        string Message
    ) : RegisterUserResult;

    public sealed record InvalidPassword(
        IReadOnlyList<string> Messages
    ) : RegisterUserResult;

    public sealed record EmailAlreadyExists(
        string ConflictingEmail
    ) : RegisterUserResult;
}
