namespace AnalyticDashboard.Api.Contracts.Auth;

public sealed record ResetPasswordRequest(
    Guid UserId,
    string Token,
    string NewPassword
);
