using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.GetWidgets;

public sealed record GetWidgetsResponse(
    Guid Id,
    Guid DashboardId,
    Guid DatasetId,
    WidgetType Type,
    string Title,
    string GroupByColumn,
    string MeasureColumn,
    AggregationType Aggregation,
    DateTime CreatedAtUtc
);
