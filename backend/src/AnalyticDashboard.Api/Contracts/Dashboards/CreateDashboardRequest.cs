namespace AnalyticDashboard.Api.Contracts.Dashboards;

public sealed record CreateDashboardRequest(
    Guid DatasetId,
    string Name
);
