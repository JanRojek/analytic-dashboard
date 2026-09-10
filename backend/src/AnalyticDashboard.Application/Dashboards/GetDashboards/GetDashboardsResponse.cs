namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed record GetDashboardsResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTime CreatedAtUtc
);
