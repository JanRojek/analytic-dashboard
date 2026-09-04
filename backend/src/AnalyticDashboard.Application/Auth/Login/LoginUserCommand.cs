namespace AnalyticDashboard.Application.Auth.Login;

public sealed record LoginUserCommand(
    string Email,
    string Password,
    bool RememberMe
);
