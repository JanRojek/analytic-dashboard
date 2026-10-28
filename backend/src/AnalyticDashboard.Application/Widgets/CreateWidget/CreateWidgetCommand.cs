using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Application.Widgets.CreateWidget;

public sealed record CreateWidgetCommand(
    Guid ProjectId,
    Guid OwnerId,
    Guid DashboardId,
    Guid DatasetId,
    WidgetType Type,
    string Title,
    string GroupByColumn,
    string MeasureColumn,
    AggregationType Aggregation,
    WidgetLayout Layout
);
