using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed class GetDashboardsHandler
{
    private readonly IDashboardRepository _repository;

    public GetDashboardsHandler(IDashboardRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GetDashboardsResponse>> Handle(
        GetDashboardsQuery query, 
        CancellationToken cancellationToken)
    {
        var dashboards = await _repository.GetAllAsync(cancellationToken);

        return dashboards.Select(d => new GetDashboardsResponse(
            d.Id,
            d.DatasetId,
            d.Dataset!.Name,
            d.Name,
            d.CreatedAtUtc
        )).ToList();
    }
}