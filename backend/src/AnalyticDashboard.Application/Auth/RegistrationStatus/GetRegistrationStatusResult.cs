namespace AnalyticDashboard.Application.Auth.RegistrationStatus;

public abstract record GetRegistrationStatusResult
{
    public sealed record Pending
        : GetRegistrationStatusResult;

    public sealed record Confirmed
        : GetRegistrationStatusResult;

    public sealed record UserNotFound
        : GetRegistrationStatusResult;
}
