using AnalyticDashboard.Application.Projects.Persistence;
using System.Diagnostics;

namespace AnalyticDashboard.Application.Projects.DeleteProject;

public sealed class DeleteProjectHandler
{
    private readonly IProjectRepository _projectRepository;

    public DeleteProjectHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<DeleteProjectResult> HandleAsync(
        DeleteProjectCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await _projectRepository.DeleteByIdAndOwnerAsync(
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        return outcome switch
        {
            ProjectDeleteOutcome.Deleted =>
                new DeleteProjectResult.Success(),

            ProjectDeleteOutcome.NotFound =>
                new DeleteProjectResult.NotFound(),

            _ => throw new UnreachableException()
        };
    }
}
