using System.Diagnostics;

using AnalyticDashboard.Application.Projects.Persistence;

namespace AnalyticDashboard.Application.Projects.DeleteProject;

public sealed class DeleteProjectHandler
{
    private readonly IProjectDeletionStore _deletionStore;

    public DeleteProjectHandler(IProjectDeletionStore deletionStore)
    {
        _deletionStore = deletionStore;
    }

    public async Task<DeleteProjectResult> HandleAsync(
        DeleteProjectCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await _deletionStore.DeleteAsync(
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        return outcome switch
        {
            ProjectDeletionOutcome.Deleted =>
                new DeleteProjectResult.Success(),

            ProjectDeletionOutcome.NotFound =>
                new DeleteProjectResult.NotFound(),

            ProjectDeletionOutcome.ImportInProgress =>
                new DeleteProjectResult.ImportInProgress(),

            _ => throw new UnreachableException()
        };
    }
}
