using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IProjectRepository
{
    Task<bool> ExistsByOwnerAndNameAsync(
        Guid ownerId,
        string name,
        CancellationToken cancellationToken);

    Task AddAsync(
        Project project,
        CancellationToken cancellationToken);
}
