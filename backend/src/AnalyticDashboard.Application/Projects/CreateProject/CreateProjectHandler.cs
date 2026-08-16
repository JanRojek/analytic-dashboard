using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Projects.CreateProject;

public sealed class CreateProjectHandler
{
    private readonly IProjectRepository _projectRepository;

    public CreateProjectHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<CreateProjectResult> HandleAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var project = new Project(
            command.OwnerId,
            command.Name
        );

        var exists = await _projectRepository.ExistsByOwnerAndNameAsync(
            project.OwnerId,
            project.Name,
            cancellationToken
        );

        if (exists)
        {
            return new CreateProjectResult.NameAlreadyExists(project.Name);
        }

        await _projectRepository.AddAsync(
            project,
            cancellationToken
        );

        return new CreateProjectResult.Success(
            project.Id,
            project.Name,
            project.CreatedAt
        );
    }
}
