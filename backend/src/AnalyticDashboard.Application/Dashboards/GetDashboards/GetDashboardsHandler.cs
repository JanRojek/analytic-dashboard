using AnalyticDashboard.Application.Dashboards.Persistence;

namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed class GetDashboardsHandler
{
    private readonly IDashboardRepository _dashboardRepository;

    public GetDashboardsHandler(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<IReadOnlyList<GetDashboardsResponse>> HandleAsync(
        GetDashboardsQuery query,
        CancellationToken cancellationToken)
    {
        var dashboards =
            await _dashboardRepository.GetAllByProjectAndOwnerAsync(
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        return dashboards.Select(dashboard => new GetDashboardsResponse(
            dashboard.Id,
            dashboard.ProjectId,
            dashboard.Name,
            dashboard.CreatedAtUtc
        )).ToList();
    }
}
