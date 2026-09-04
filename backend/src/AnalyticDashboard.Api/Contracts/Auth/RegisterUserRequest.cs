namespace AnalyticDashboard.Api.Contracts.Auth;

public sealed record RegisterUserRequest(
    string Email,
    string DisplayName,
    string Password
);
