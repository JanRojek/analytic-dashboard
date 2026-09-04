namespace AnalyticDashboard.Application.Auth.ResetPassword;

public sealed record ResetPasswordCommand(
    Guid UserId,
    string Token,
    string NewPassword
);
