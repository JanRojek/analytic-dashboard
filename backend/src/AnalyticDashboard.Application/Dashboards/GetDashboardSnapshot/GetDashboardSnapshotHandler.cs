using AnalyticDashboard.Application.Dashboards.Persistence;

namespace AnalyticDashboard.Application.Dashboards.GetDashboardSnapshot;

public sealed class GetDashboardSnapshotHandler
{
    private readonly IDashboardRepository _dashboardRepository;

    public GetDashboardSnapshotHandler(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<GetDashboardSnapshotResult> HandleAsync(
        GetDashboardSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        var snapshot = await _dashboardRepository.GetEditorSnapshotAsync(
            query.DashboardId,
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (snapshot is null)
        {
            return new GetDashboardSnapshotResult.NotFound();
        }

        var widgets = snapshot.Widgets
            .Select(widget => new WidgetSnapshotItem(
                widget.Id,
                widget.DatasetId,
                widget.Type,
                widget.Title,
                widget.GroupByColumn,
                widget.MeasureColumn,
                widget.Aggregation,
                new WidgetLayoutSnapshot(
                    widget.Layout.X,
                    widget.Layout.Y,
                    widget.Layout.Width,
                    widget.Layout.Height
                ),
                widget.CreatedAtUtc
            ))
            .ToArray();

        return new GetDashboardSnapshotResult.Success(
            snapshot.Dashboard.Id,
            snapshot.Dashboard.Name,
            snapshot.Dashboard.Revision,
            widgets
        );
    }
}
