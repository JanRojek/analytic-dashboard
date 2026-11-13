using AnalyticDashboard.Application.Datasets.Persistence;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobRecoveryService
{
    private readonly IImportJobRepository _importJobRepository;
    private readonly IDatasetVersionRepository _versionRepository;

    public ImportJobRecoveryService(
        IImportJobRepository importJobRepository,
        IDatasetVersionRepository versionRepository)
    {
        _importJobRepository = importJobRepository;
        _versionRepository = versionRepository;
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

            datasetVersion.MarkFailed();
            job.MarkFailed("Import interrupted by application restart.");

            await _importJobRepository.SaveChangesAsync(
                cancellationToken
            );
        }
    }
}
