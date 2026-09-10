using AnalyticDashboard.Application.Dashboards.Persistence;

namespace AnalyticDashboard.Application.Dashboards.DeleteDashboard;

public sealed class DeleteDashboardHandler
{
    private readonly IDashboardRepository _dashboardRepository;

    public DeleteDashboardHandler(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<bool> HandleAsync(
        DeleteDashboardCommand command,
        CancellationToken cancellationToken)
    {
        return await _dashboardRepository.DeleteByIdAndProjectOwnerAsync(
            command.Id,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );
    }
}
