using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Api.Contracts.Widgets;

public sealed record UpdateDashboardWidgetsRequest(
    IReadOnlyList<DashboardWidgetUpdateRequest?>? Widgets
);

public sealed record DashboardWidgetUpdateRequest(
    Guid WidgetId,
    Guid DatasetId,
    WidgetType Type,
    string? Title,
    string? GroupByColumn,
    string? MeasureColumn,
    string? Aggregation,
    WidgetLayoutRequest? Layout
);
