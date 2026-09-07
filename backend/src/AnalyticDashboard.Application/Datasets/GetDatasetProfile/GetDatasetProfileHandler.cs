using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Profiling;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.GetDatasetProfile;

public sealed class GetDatasetProfileHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IDatasetProfileReader _profileReader;

    public GetDatasetProfileHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IDatasetProfileReader profileReader)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _profileReader = profileReader;
    }

    public async Task<GetDatasetProfileResult> HandleAsync(
        GetDatasetProfileQuery query,
        CancellationToken cancellationToken)
    {
        var dataset =
            await _datasetRepository.GetByIdAndProjectOwnerAsync(
                query.DatasetId,
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        if (dataset is not { CurrentVersionId: { } versionId })
        {
            return new GetDatasetProfileResult.NotFound();
        }

        var version = await _versionRepository.GetByIdAsync(
            versionId,
            dataset.Id,
            cancellationToken
        );

        if (version is null
            || version.Status != DatasetVersionStatus.Ready
            || !version.RowCount.HasValue
            || !version.ColumnCount.HasValue)
        {
            return new GetDatasetProfileResult.NotFound();
        }

        if (!File.Exists(version.StorageKey))
        {
            throw new FileNotFoundException(
                "Dataset file not found.",
                version.StorageKey
            );
        }

        var profile = await _profileReader.ReadProfileAsync(
            dataset.Id,
            dataset.Name,
            version.OriginalFileName,
            version.StorageKey,
            version.RowCount.Value,
            version.ColumnCount.Value,
            cancellationToken
        );

        return new GetDatasetProfileResult.Found(
            profile
        );
    }
}
