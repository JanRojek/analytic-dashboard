using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Application.Widgets.UpdateDashboardWidgets;

public sealed record UpdateDashboardWidgetsCommand(
    Guid ProjectId,
    Guid DashboardId,
    Guid OwnerId,
    IReadOnlyList<DashboardWidgetUpdate> Widgets
);

public sealed record DashboardWidgetUpdate(
    Guid WidgetId,
    Guid DatasetId,
    WidgetType Type,
    string Title,
    string GroupByColumn,
    string MeasureColumn,
    AggregationType Aggregation,
    WidgetLayout Layout
);
