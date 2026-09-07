using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public sealed class GetDatasetsHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IProjectRepository _projectRepository;

    public GetDatasetsHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IProjectRepository projectRepository)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _projectRepository = projectRepository;
    }

    public async Task<GetDatasetsResult> HandleAsync(
        GetDatasetsQuery query,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (project is null)
        {
            return new GetDatasetsResult.NotFound();
        }

        var datasets =
            await _datasetRepository.GetAllByProjectAndOwnerAsync(
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        var currentVersionIds = datasets
            .Where(dataset => dataset.CurrentVersionId.HasValue)
            .Select(dataset => dataset.CurrentVersionId!.Value)
            .ToArray();

        var versions = await _versionRepository.GetByIdsAsync(
            currentVersionIds,
            cancellationToken
        );

        var versionsById = versions.ToDictionary(
            version => version.Id
        );

        var items = datasets
            .Select(dataset =>
            {
                GetDatasetsResult.CurrentVersion? currentVersion = null;

                if (dataset.CurrentVersionId is { } versionId
                    && versionsById.TryGetValue(
                        versionId,
                        out var version)
                    && version.DatasetId == dataset.Id
                    && version is
                    {
                        Status: DatasetVersionStatus.Ready,
                        RowCount: { } rowCount,
                        ColumnCount: { } columnCount
                    })
                {
                    currentVersion =
                        new GetDatasetsResult.CurrentVersion(
                            version.Id,
                            version.VersionNumber,
                            version.OriginalFileName,
                            rowCount,
                            columnCount
                        );
                }

                return new GetDatasetsResult.Item(
                    dataset.Id,
                    dataset.Name,
                    dataset.CreatedAtUtc,
                    currentVersion
                );
            })
            .ToList();

        return new GetDatasetsResult.Success(
            items
        );
    }
}
