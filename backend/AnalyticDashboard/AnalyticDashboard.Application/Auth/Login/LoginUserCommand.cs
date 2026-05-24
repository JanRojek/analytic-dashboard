namespace AnalyticDashboard.Application.Auth.Login;

public sealed record LoginUserCommand(
    string Username, 
    string Password 
);