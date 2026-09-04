namespace AnalyticDashboard.Api.Contracts.Auth;

public sealed record GetRegistrationStatusResponse(
    RegistrationStatus Status
);

public enum RegistrationStatus
{
    Pending,
    Confirmed
}
