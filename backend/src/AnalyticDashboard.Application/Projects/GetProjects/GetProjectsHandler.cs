using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Projects.GetProjects;

public sealed class GetProjectsHandler
{
    private readonly IProjectRepository _projectRepository;

    public GetProjectsHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<IReadOnlyList<GetProjectsItem>> HandleAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken)
    {
        var projects = await _projectRepository.GetByOwnerAsync(
            query.OwnerId,
            cancellationToken
        );

        return projects
            .Select(project => new GetProjectsItem(
                project.Id,
                project.Name,
                project.CreatedAt
            ))
            .ToList();
    }
}
