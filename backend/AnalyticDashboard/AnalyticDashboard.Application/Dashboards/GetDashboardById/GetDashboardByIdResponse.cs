namespace AnalyticDashboard.Application.Dashboards.GetDashboardById;

public sealed record GetDashboardByIdResponse(
    Guid Id,
    Guid DatasetId,
    string DatasetName,
    string Name,
    DateTime CreatedAtUtc
);