namespace AnalyticDashboard.Application.Dashboards.CreateDashboard;

public sealed record CreateDashboardCommand(
    Guid ProjectId,
    Guid OwnerId,
    Guid DatasetId,
    string Name
);
