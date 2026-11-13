using AnalyticDashboard.Application.Common.Exceptions;
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

    public async Task<CreateDashboardResult> HandleAsync(
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
            return new CreateDashboardResult.ProjectNotFound();
        }

        Dashboard dashboard;

        try
        {
            dashboard = new Dashboard(
                command.ProjectId,
                command.Name
            );
        }
        catch (InvalidEntityNameException exception)
            when (exception.EntityType == nameof(Dashboard))
        {
            return new CreateDashboardResult.InvalidName(
                exception.Message
            );
        }

        try
        {
            await _dashboardRepository.AddAsync(dashboard, cancellationToken);
        }
        catch (MissingReferenceException exception) when (exception.EntityType == nameof(Project))
        {
            return new CreateDashboardResult.ProjectNotFound();
        }

        return new CreateDashboardResult.Success(
            dashboard.Id
        );
    }
}
