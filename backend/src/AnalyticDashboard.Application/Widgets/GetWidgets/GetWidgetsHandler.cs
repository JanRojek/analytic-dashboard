using AnalyticDashboard.Application.Widgets.Persistence;

namespace AnalyticDashboard.Application.Widgets.GetWidgets;

public sealed class GetWidgetsHandler
{
    private readonly IWidgetRepository _widgetRepository;

    public GetWidgetsHandler(IWidgetRepository widgetRepository)
    {
        _widgetRepository = widgetRepository;
    }

    public async Task<IReadOnlyList<GetWidgetsResponse>> HandleAsync(
        GetWidgetsQuery query,
        CancellationToken cancellationToken)
    {
        var widgets =
            await _widgetRepository.GetAllByDashboardProjectOwnerAsync(
                query.DashboardId,
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        return widgets.Select(widget => new GetWidgetsResponse(
            widget.Id,
            widget.DashboardId,
            widget.DatasetId,
            widget.Type,
            widget.Title,
            widget.GroupByColumn,
            widget.MeasureColumn,
            widget.Aggregation,
            widget.CreatedAtUtc
        )).ToList();
    }
}
