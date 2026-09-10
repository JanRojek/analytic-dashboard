namespace AnalyticDashboard.Application.Dashboards.CreateDashboard;

public sealed record CreateDashboardCommand(
    Guid ProjectId,
    Guid OwnerId,
    string Name
);
