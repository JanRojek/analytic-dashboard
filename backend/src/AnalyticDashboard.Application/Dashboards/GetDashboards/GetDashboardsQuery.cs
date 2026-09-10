namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed record GetDashboardsQuery(
    Guid ProjectId,
    Guid OwnerId
);
