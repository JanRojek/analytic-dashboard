namespace AnalyticDashboard.Application.Dashboards.CreateDashboard;

public sealed record CreateDashboardCommand(
    Guid DatasetId,
    string Name
);