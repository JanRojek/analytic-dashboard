using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Api.Contracts.Dashboards;

public sealed record GetDashboardSnapshotResponse(
    Guid DashboardId,
    string Name,
    long Revision,
    IReadOnlyList<WidgetSnapshotResponse> Widgets
);

public sealed record WidgetSnapshotResponse(
    Guid Id,
    Guid? DatasetId,
    WidgetType Type,
    string Title,
    string GroupByColumn,
    string MeasureColumn,
    AggregationType Aggregation,
    WidgetSnapshotLayoutResponse Layout,
    DateTime CreatedAtUtc
);

public sealed record WidgetSnapshotLayoutResponse(
    int X,
    int Y,
    int Width,
    int Height
);
