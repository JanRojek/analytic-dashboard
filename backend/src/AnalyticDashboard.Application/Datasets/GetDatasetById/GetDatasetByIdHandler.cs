using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.GetDatasetById;

public sealed class GetDatasetByIdHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;

    public GetDatasetByIdHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
    }

    public async Task<GetDatasetByIdResult> HandleAsync(
        GetDatasetByIdQuery query,
        CancellationToken cancellationToken)
    {
        var dataset =
            await _datasetRepository.GetByIdAndProjectOwnerAsync(
                query.DatasetId,
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        if (dataset is null)
        {
            return new GetDatasetByIdResult.NotFound();
        }

        GetDatasetByIdResult.CurrentVersion? currentVersion = null;

        if (dataset.CurrentVersionId is { } versionId)
        {
            var version = await _versionRepository.GetByIdAsync(
                versionId,
                dataset.Id,
                cancellationToken
            );

            if (version is
                {
                    Status: DatasetVersionStatus.Ready,
                    RowCount: { } rowCount,
                    ColumnCount: { } columnCount
                })
            {
                currentVersion =
                    new GetDatasetByIdResult.CurrentVersion(
                        version.Id,
                        version.VersionNumber,
                        version.OriginalFileName,
                        rowCount,
                        columnCount
                    );
            }
        }

        return new GetDatasetByIdResult.Found(
            dataset.Id,
            dataset.Name,
            dataset.CreatedAtUtc,
            currentVersion
        );
    }
}
