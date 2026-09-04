namespace AnalyticDashboard.Application.Auth.Register;

public sealed record RegisterUserCommand(
    string Email,
    string DisplayName,
    string Password
);
