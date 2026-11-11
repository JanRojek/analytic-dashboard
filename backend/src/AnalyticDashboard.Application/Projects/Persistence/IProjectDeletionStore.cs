namespace AnalyticDashboard.Application.Projects.Persistence;

public interface IProjectDeletionStore
{
    Task<ProjectDeletionOutcome> DeleteAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );
}

public enum ProjectDeletionOutcome
{
    Deleted,
    NotFound,
    ImportInProgress
}
