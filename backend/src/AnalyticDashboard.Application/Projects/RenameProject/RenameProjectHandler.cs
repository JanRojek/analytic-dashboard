using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using System.Diagnostics;

namespace AnalyticDashboard.Application.Projects.RenameProject;

public sealed class RenameProjectHandler
{
    private readonly IProjectRepository _projectRepository;

    public RenameProjectHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<RenameProjectResult> HandleAsync(
        RenameProjectCommand command,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (project == null)
        {
            return new RenameProjectResult.NotFound();
        }

        try
        {
            project.Rename(command.Name);
        }
        catch (InvalidProjectNameException exception)
        {
            return new RenameProjectResult.InvalidName(
                exception.Message
            );
        }

        var outcome = await _projectRepository.RenameAsync(
            project.Id,
            project.OwnerId,
            project.Name,
            cancellationToken
        );

        return outcome switch
        {
            ProjectRenameOutcome.Renamed =>
                new RenameProjectResult.Success(
                    project.Id,
                    project.Name,
                    project.CreatedAtUtc
                ),

            ProjectRenameOutcome.NotFound =>
                new RenameProjectResult.NotFound(),

            ProjectRenameOutcome.NameAlreadyExists =>
                new RenameProjectResult.NameAlreadyExists(
                    project.Name
                ),

            _ => throw new UnreachableException()
        };
    }
}
