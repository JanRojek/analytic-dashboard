using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Widgets.GetWidgets;

public sealed class GetWidgetsHandler
{
    private readonly IWidgetRepository _repository;

    public GetWidgetsHandler(IWidgetRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GetWidgetsResponse>> Handle(
        GetWidgetsQuery query, 
        CancellationToken cancellationToken)
    {
        var widgets = await _repository.GetByDashboardIdAsync(
            query.DashboardId, 
            cancellationToken
        );

        return widgets.Select(w => new GetWidgetsResponse(
            w.Id,
            w.DashboardId,
            w.Type,
            w.Title,
            w.XColumn,
            w.YColumn,
            w.Aggregation,
            w.CreatedAtUtc
        )).ToList();
    }
}