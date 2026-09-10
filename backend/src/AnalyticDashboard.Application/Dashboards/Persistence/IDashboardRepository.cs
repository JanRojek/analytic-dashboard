using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Dashboards.Persistence;

public interface IDashboardRepository
{
    Task AddAsync(
        Dashboard dashboard,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<Dashboard>> GetAllByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<Dashboard?> GetByIdAndProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<bool> DeleteByIdAndProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );
}
