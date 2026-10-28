using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Application.Dashboards.UpdateDashboardLayout;

public sealed record UpdateDashboardLayoutCommand(
    Guid ProjectId,
    Guid DashboardId,
    Guid OwnerId,
    IReadOnlyList<WidgetLayoutChange> WidgetLayoutChanges
);

public sealed record WidgetLayoutChange(
    Guid WidgetId,
    WidgetLayout NewLayout
);
