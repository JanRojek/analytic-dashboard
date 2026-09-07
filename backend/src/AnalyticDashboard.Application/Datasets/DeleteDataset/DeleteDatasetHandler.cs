using AnalyticDashboard.Application.Datasets.Persistence;

namespace AnalyticDashboard.Application.Datasets.DeleteDataset;

public sealed class DeleteDatasetHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;

    public DeleteDatasetHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
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
            if (File.Exists(version.StorageKey))
            {
                File.Delete(version.StorageKey);
            }
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
