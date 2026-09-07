using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IDatasetVersionRepository
{
    Task AddAsync(
        DatasetVersion version,
        CancellationToken cancellationToken
    );

    Task<DatasetVersion?> GetByIdAsync(
        Guid versionId,
        Guid datasetId,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<DatasetVersion>> GetByIdsAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<DatasetVersion>> GetAllByDatasetIdAsync(
        Guid datasetId,
        CancellationToken cancellationToken
    );
}
