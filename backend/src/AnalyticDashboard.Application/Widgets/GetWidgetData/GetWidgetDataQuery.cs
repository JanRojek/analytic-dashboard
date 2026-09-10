namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public sealed record GetWidgetDataQuery(
    Guid WidgetId,
    Guid DashboardId,
    Guid ProjectId,
    Guid OwnerId
);
