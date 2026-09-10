namespace AnalyticDashboard.Application.Dashboards.GetDashboardById;

public sealed record GetDashboardByIdQuery(
    Guid Id,
    Guid ProjectId,
    Guid OwnerId
);
