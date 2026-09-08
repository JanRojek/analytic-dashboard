using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public sealed class QueryDatasetHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IDatasetQueryReader _queryReader;

    public QueryDatasetHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IDatasetQueryReader queryReader)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _queryReader = queryReader;
    }

    public async Task<QueryDatasetResult> HandleAsync(
        QueryDatasetQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.GroupByColumn))
        {
            return new QueryDatasetResult.InvalidQuery(
                "Group by column cannot be empty."
            );
        }

        if (string.IsNullOrWhiteSpace(query.MeasureColumn))
        {
            return new QueryDatasetResult.InvalidQuery(
                "Measure column cannot be empty."
            );
        }

        var dataset = await _datasetRepository.GetByIdAndProjectOwnerAsync(
            query.DatasetId,
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (dataset is null)
        {
            return new QueryDatasetResult.NotFound();
        }

        if (dataset.CurrentVersionId is not { } versionId)
        {
            return new QueryDatasetResult.InvalidQuery(
                "Dataset has no ready version."
            );
        }

        var version = await _versionRepository.GetByIdAsync(
            versionId,
            dataset.Id,
            cancellationToken
        );

        if (version is null
            || version.Status != DatasetVersionStatus.Ready
            || version.StorageKey is null)
        {
            return new QueryDatasetResult.InvalidQuery(
                "Dataset has no ready version."
            );
        }

        var readResult = await _queryReader.ReadAsync(
            version.StorageKey,
            query.GroupByColumn.Trim(),
            query.MeasureColumn.Trim(),
            query.Aggregation,
            cancellationToken
        );

        return readResult switch
        {
            DatasetQueryReadResult.Success success =>
                new QueryDatasetResult.Success(
                    success.Rows
                        .Select(row =>
                            new QueryDatasetResult.Item(
                                row.Label,
                                row.Value
                            )
                        )
                        .ToList()
                ),

            DatasetQueryReadResult.InvalidQuery invalid =>
                new QueryDatasetResult.InvalidQuery(
                    invalid.Message
                ),

            _ => throw new InvalidOperationException(
                "Unsupported dataset query result."
            )
        };
    }
}
