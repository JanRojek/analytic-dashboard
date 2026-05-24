namespace AnalyticDashboard.Application.Auth.Register;

public sealed record RegisterUserCommand(
    string Username, 
    string Password
);