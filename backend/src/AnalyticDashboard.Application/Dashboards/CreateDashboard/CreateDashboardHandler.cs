using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Dashboards.CreateDashboard;

public sealed class CreateDashboardHandler
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IProjectRepository _projectRepository;

    public CreateDashboardHandler(
        IDashboardRepository dashboardRepository,
        IProjectRepository projectRepository)
    {
        _dashboardRepository = dashboardRepository;
        _projectRepository = projectRepository;
    }

    public async Task<CreateDashboardResponse?> HandleAsync(
        CreateDashboardCommand command,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (project is null)
        {
            return null;
        }

        var dashboard = new Dashboard(
            command.ProjectId,
            command.Name
        );

        await _dashboardRepository.AddAsync(
            dashboard,
            cancellationToken
        );

        return new CreateDashboardResponse(
            dashboard.Id
        );
    }
}
