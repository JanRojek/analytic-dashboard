namespace AnalyticDashboard.Api.Contracts.Auth;

public sealed record GetCurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    DateTime CreatedAtUtc
);
