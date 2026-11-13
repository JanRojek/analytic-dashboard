using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IDatasetRepository
{
    Task<IReadOnlyList<Dataset>> GetAllByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
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
}
