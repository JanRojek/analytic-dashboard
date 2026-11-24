namespace AnalyticDashboard.Application.Dashboards.GetDashboardSnapshot;

public sealed record GetDashboardSnapshotQuery(
    Guid DashboardId,
    Guid ProjectId,
    Guid OwnerId
);
