using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Dashboards.GetDashboardSnapshot;

public abstract record GetDashboardSnapshotResult
{
    public sealed record Success(
        Guid DashboardId,
        string Name,
        long Revision,
        IReadOnlyList<WidgetSnapshotItem> Widgets
    ) : GetDashboardSnapshotResult;

    public sealed record NotFound : GetDashboardSnapshotResult;
}

public sealed record WidgetLayoutSnapshot(
    int X,
    int Y,
    int Width,
    int Height
);

public sealed record WidgetSnapshotItem(
    Guid Id,
    Guid? DatasetId,
    WidgetType Type,
    string Title,
    string GroupByColumn,
    string MeasureColumn,
    AggregationType Aggregation,
    WidgetLayoutSnapshot Layout,
    DateTime CreatedAtUtc
);
