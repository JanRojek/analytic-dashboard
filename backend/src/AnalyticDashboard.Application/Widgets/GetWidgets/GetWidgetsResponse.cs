using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.GetWidgets;

public sealed record GetWidgetsResponse(
    Guid Id,
    Guid DashboardId,
    WidgetType Type,
    string Title,
    string? XColumn,
    string? YColumn,
    string Aggregation,
    DateTime CreatedAtUtc
);