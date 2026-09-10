namespace AnalyticDashboard.Application.Widgets.DeleteWidget;

public sealed record DeleteWidgetCommand(
    Guid Id,
    Guid DashboardId,
    Guid ProjectId,
    Guid OwnerId
);
