namespace AnalyticDashboard.Api.Contracts.Auth;

public sealed record RegisterUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    DateTime CreatedAtUtc
);
