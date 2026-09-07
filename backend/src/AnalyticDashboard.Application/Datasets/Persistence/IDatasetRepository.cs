using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IDatasetRepository
{
    Task AddAsync(
        Dataset dataset,
        CancellationToken cancellationToken
    );

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

    Task<bool> DeleteByIdAndProjectOwnerAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<bool> PublishVersionAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        Guid versionId,
        CancellationToken cancellationToken
    );
}
