using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IDatasetRepository
{
    Task<int> CountByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        string? search,
        bool readyOnly,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<Dataset>> GetPageByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        string? search,
        bool readyOnly,
        int skip,
        int take,
        CancellationToken cancellationToken
    );

    Task<Dataset?> GetByIdAndProjectOwnerAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<HashSet<Guid>> GetExistingDatasetIdsAsync(
        IReadOnlyCollection<Guid> datasetIds,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<DatasetSummary> GetSummaryByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );
}
