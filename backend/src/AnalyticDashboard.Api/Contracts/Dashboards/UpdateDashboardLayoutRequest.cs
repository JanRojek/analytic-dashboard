using AnalyticDashboard.Api.Contracts.Widgets;

namespace AnalyticDashboard.Api.Contracts.Dashboards;

public sealed record UpdateDashboardLayoutRequest(
    IReadOnlyList<WidgetPlacementRequest>? Widgets
);

public sealed record WidgetPlacementRequest(
    Guid WidgetId,
    WidgetLayoutRequest? Layout
);
