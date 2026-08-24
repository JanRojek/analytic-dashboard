using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IProjectRepository
{
    Task<ProjectAddOutcome> AddAsync(
        Project project,
        CancellationToken cancellationToken
    );

    Task<Project?> GetByIdAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<ProjectRenameOutcome> RenameAsync(
        Guid projectId,
        Guid ownerId,
        string name,
        CancellationToken cancellationToken
    );

    Task<ProjectDeleteOutcome> DeleteByIdAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<int> CountByOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<Project>> GetPageByOwnerAsync(
        Guid ownerId,
        int skip,
        int take,
        CancellationToken cancellationToken
    );
}
