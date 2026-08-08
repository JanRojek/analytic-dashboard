namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed record GetDashboardsResponse(
    Guid Id,
    Guid DatasetId,
    string DatasetName,
    string Name,
    DateTime CreatedAtUtc
);