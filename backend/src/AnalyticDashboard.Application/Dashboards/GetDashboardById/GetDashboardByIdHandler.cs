using AnalyticDashboard.Application.Dashboards.Persistence;

namespace AnalyticDashboard.Application.Dashboards.GetDashboardById;

public class GetDashboardByIdHandler
{
    private readonly IDashboardRepository _dashboardRepository;

    public GetDashboardByIdHandler(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<GetDashboardByIdResponse?> HandleAsync(
        GetDashboardByIdQuery query,
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardRepository.GetByIdAndProjectOwnerAsync(
            query.Id,
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (dashboard == null)
        {
            return null;
        }

        return new GetDashboardByIdResponse(
            dashboard.Id,
            dashboard.ProjectId,
            dashboard.Name,
            dashboard.CreatedAtUtc
        );
    }
}
