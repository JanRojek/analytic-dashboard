using AnalyticDashboard.Application.Datasets.Persistence;

namespace AnalyticDashboard.Application.Datasets.GetImportStatus;

public sealed class GetImportStatusHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IImportJobRepository _importJobRepository;

    public GetImportStatusHandler(
        IDatasetRepository datasetRepository,
        IImportJobRepository importJobRepository)
    {
        _datasetRepository = datasetRepository;
        _importJobRepository = importJobRepository;
    }

    public async Task<GetImportStatusResult> HandleAsync(
        GetImportStatusQuery query,
        CancellationToken cancellationToken)
    {
        var dataset = await _datasetRepository.GetByIdAndProjectOwnerAsync(
            query.DatasetId,
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (dataset is null)
        {
            return new GetImportStatusResult.NotFound();
        }

        var importJob = await _importJobRepository.GetLatestByDatasetIdAsync(
            dataset.Id,
            cancellationToken
        );

        return importJob is null
            ? new GetImportStatusResult.NotFound()
            : new GetImportStatusResult.Found(
                importJob.Id,
                importJob.DatasetVersionId,
                importJob.Status,
                importJob.CreatedAtUtc,
                importJob.StartedAtUtc,
                importJob.CompletedAtUtc
            );
    }
}
