using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Dashboards.Persistence;

public interface IDashboardRepository
{
    Task AddAsync(
        Dashboard dashboard,
        CancellationToken cancellationToken
    );

    Task<DashboardEditorSnapshot?> GetEditorSnapshotAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<Dashboard?> GetForUpdateByIdAndProjectOwnerAsync(
        Guid dashboardId,
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

    Task<long?> GetDashboardCurrentRevisionAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<int> CountByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<Dashboard>> GetPageByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        int skip,
        int take,
        CancellationToken cancellationToken
    );
}
