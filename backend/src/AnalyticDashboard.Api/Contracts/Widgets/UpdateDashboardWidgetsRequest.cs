using System.Text.Json.Serialization;

using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Api.Contracts.Widgets;

public sealed record UpdateDashboardWidgetsRequest(
    IReadOnlyList<DashboardWidgetUpdateRequest?>? Widgets,
    long? ExpectedRevision
);

public sealed record DashboardWidgetUpdateRequest(
    Guid WidgetId,
    [property: JsonRequired] Guid? DatasetId,
    WidgetType Type,
    string? Title,
    string? GroupByColumn,
    string? MeasureColumn,
    string? Aggregation,
    WidgetLayoutRequest? Layout
);
