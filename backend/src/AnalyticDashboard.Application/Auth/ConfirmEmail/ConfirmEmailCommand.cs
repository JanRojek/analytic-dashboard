namespace AnalyticDashboard.Application.Auth.ConfirmEmail;

public sealed record ConfirmEmailCommand(
    Guid UserId,
    string Token
);
