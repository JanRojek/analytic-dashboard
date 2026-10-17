using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.GetDatasetRows;

public sealed class GetDatasetRowsHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IDatasetRowsReader _datasetRowsReader;

    public GetDatasetRowsHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IDatasetRowsReader datasetRowsReader)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _datasetRowsReader = datasetRowsReader;
    }

    public async Task<GetDatasetRowsResult> HandleAsync(
        GetDatasetRowsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > GetDatasetRowsQuery.MaxPageSize)
        {
            return new GetDatasetRowsResult.InvalidPageSize(
                $"Page size must be between 1 and {GetDatasetRowsQuery.MaxPageSize}."
            );
        }

        var dataset =
            await _datasetRepository.GetByIdAndProjectOwnerAsync(
                query.DatasetId,
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        if (dataset is not { CurrentVersionId: { } versionId})
        {
            return new GetDatasetRowsResult.NotFound();
        }

        var version = await _versionRepository.GetByIdAsync(
            versionId,
            dataset.Id,
            cancellationToken
        );

        if (version?.Status != DatasetVersionStatus.Ready
            || version.StorageKey == null
            || version.RowCount == null)
        {
            return new GetDatasetRowsResult.NotFound();
        }

        var normalizedPage = query.Page <= 0
            ? GetDatasetRowsQuery.DefaultPage
            : query.Page;

        var totalRows = version.RowCount.Value;

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                totalRows / (double)query.PageSize
            )
        );

        var actualPage = Math.Min(
            normalizedPage,
            totalPages
        );

        var offset = (actualPage - 1) * query.PageSize;

        var items = await _datasetRowsReader.ReadDatasetRowsAsync(
            version.StorageKey,
            offset,
            query.PageSize,
            cancellationToken
        );

        return new GetDatasetRowsResult.Success(
            items,
            actualPage,
            query.PageSize,
            totalRows,
            totalPages
        );
    }
}
