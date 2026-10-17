using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobRecoveryService
{
    private readonly IImportJobRepository _importJobRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IFileStorage _fileStorage;

    public ImportJobRecoveryService(
        IImportJobRepository importJobRepository,
        IDatasetVersionRepository versionRepository,
        IFileStorage fileStorage)
    {
        _importJobRepository = importJobRepository;
        _versionRepository = versionRepository;
        _fileStorage = fileStorage;
    }

    public async Task RecoverInterruptedAsync(
        CancellationToken cancellationToken)
    {
        var runningJobs = await _importJobRepository.GetRunningAsync(
            cancellationToken
        );

        foreach (var job in runningJobs)
        {
            var datasetVersion = await _versionRepository.GetByIdForUpdateAsync(
                job.DatasetVersionId,
                cancellationToken
            );

            if (datasetVersion is null)
            {
                throw new InvalidOperationException(
                    $"Dataset version {job.DatasetVersionId} not found."
                );
            }

            await _fileStorage.DeleteAsync(
                $"datasets/{datasetVersion.DatasetId:N}/versions/{datasetVersion.Id:N}/data.parquet",
                cancellationToken
            );

            datasetVersion.MarkFailed();
            job.MarkFailed("Import interrupted by application restart.");

            await _importJobRepository.SaveChangesAsync(
                cancellationToken
            );
        }
    }
}
