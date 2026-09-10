namespace AnalyticDashboard.Application.Dashboards.GetDashboardById;

public sealed record GetDashboardByIdResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTime CreatedAtUtc
);
