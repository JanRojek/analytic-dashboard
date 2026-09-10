namespace AnalyticDashboard.Application.Widgets.GetWidgets;

public sealed record GetWidgetsQuery(
    Guid DashboardId,
    Guid ProjectId,
    Guid OwnerId
);
