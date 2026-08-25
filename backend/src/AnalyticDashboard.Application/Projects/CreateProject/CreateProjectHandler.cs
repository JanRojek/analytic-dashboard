using System.Diagnostics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Application.Projects.Persistence;

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
        Project project;

        try
        {
            project = new Project(
                command.OwnerId,
                command.Name
            );
        }
        catch (InvalidProjectNameException exception)
        {
            return new CreateProjectResult.InvalidName(
                exception.Message
            );
        }

        var outcome = await _projectRepository.AddAsync(
            project,
            cancellationToken
        );

        return outcome switch
        {
            ProjectAddOutcome.Added =>
                new CreateProjectResult.Success(
                    project.Id,
                    project.Name,
                    project.CreatedAtUtc
                ),

            ProjectAddOutcome.NameAlreadyExists =>
                new CreateProjectResult.NameAlreadyExists(
                    project.Name
                ),

            _ => throw new UnreachableException()
        };
    }
}
