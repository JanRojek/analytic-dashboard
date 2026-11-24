namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed record GetDashboardsItem(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTime CreatedAtUtc
);
