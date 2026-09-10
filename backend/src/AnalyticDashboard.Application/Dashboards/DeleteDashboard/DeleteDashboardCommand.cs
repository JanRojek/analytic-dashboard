namespace AnalyticDashboard.Application.Dashboards.DeleteDashboard;

public sealed record DeleteDashboardCommand(
    Guid Id,
    Guid ProjectId,
    Guid OwnerId
);
