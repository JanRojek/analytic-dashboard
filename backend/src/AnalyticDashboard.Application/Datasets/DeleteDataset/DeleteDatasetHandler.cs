using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;

namespace AnalyticDashboard.Application.Datasets.DeleteDataset;

public sealed class DeleteDatasetHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IFileStorage _fileStorage;

    public DeleteDatasetHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IFileStorage fileStorage)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _fileStorage = fileStorage;
    }

    public async Task<DeleteDatasetResult> HandleAsync(
        DeleteDatasetCommand command,
        CancellationToken cancellationToken)
    {
        var dataset =
            await _datasetRepository.GetByIdAndProjectOwnerAsync(
                command.DatasetId,
                command.ProjectId,
                command.OwnerId,
                cancellationToken
            );

        if (dataset is null)
        {
            return new DeleteDatasetResult.NotFound();
        }

        var versions =
            await _versionRepository.GetAllByDatasetIdAsync(
                dataset.Id,
                cancellationToken
            );

        foreach (var version in versions)
        {
            if (version.StorageKey is null)
            {
                continue;
            }

            await _fileStorage.DeleteAsync(
                version.StorageKey,
                cancellationToken
            );
        }

        var deleted =
            await _datasetRepository.DeleteByIdAndProjectOwnerAsync(
                command.DatasetId,
                command.ProjectId,
                command.OwnerId,
                cancellationToken
            );

        return deleted
            ? new DeleteDatasetResult.Success()
            : new DeleteDatasetResult.NotFound();
    }
}
