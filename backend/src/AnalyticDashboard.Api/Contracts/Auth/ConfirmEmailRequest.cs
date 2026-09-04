namespace AnalyticDashboard.Api.Contracts.Auth;

public sealed record ConfirmEmailRequest(
    Guid UserId,
    string Token
);
