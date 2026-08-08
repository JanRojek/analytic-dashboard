using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.CreateWidget;

public sealed record CreateWidgetCommand(
    Guid DashboardId,
    WidgetType Type,
    string Title,
    string? XColumn,
    string? YColumn,
    string Aggregation
);