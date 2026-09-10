using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public sealed class DatasetQueryExecutor
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IDatasetQueryReader _queryReader;

    public DatasetQueryExecutor(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IDatasetQueryReader queryReader)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _queryReader = queryReader;
    }

    public async Task<DatasetQueryExecutionResult> ExecuteAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupByColumn))
        {
            return new DatasetQueryExecutionResult.InvalidQuery(
                "Group by column cannot be empty."
            );
        }

        if (string.IsNullOrWhiteSpace(measureColumn))
        {
            return new DatasetQueryExecutionResult.InvalidQuery(
                "Measure column cannot be empty."
            );
        }

        var dataset = await _datasetRepository.GetByIdAndProjectOwnerAsync(
            datasetId,
            projectId,
            ownerId,
            cancellationToken
        );

        if (dataset is null)
        {
            return new DatasetQueryExecutionResult.NotFound();
        }

        if (dataset.CurrentVersionId is not { } versionId)
        {
            return new DatasetQueryExecutionResult.InvalidQuery(
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
            return new DatasetQueryExecutionResult.InvalidQuery(
                "Dataset has no ready version."
            );
        }

        var readResult = await _queryReader.ReadAsync(
            version.StorageKey,
            groupByColumn.Trim(),
            measureColumn.Trim(),
            aggregation,
            cancellationToken
        );

        return readResult switch
        {
            DatasetQueryReadResult.Success success =>
                new DatasetQueryExecutionResult.Success(
                    success.Rows
                        .Select(row =>
                            new DatasetQueryExecutionResult.Item(
                                row.Label,
                                row.Value
                            )
                        )
                        .ToList()
                ),

            DatasetQueryReadResult.InvalidQuery invalid =>
                new DatasetQueryExecutionResult.InvalidQuery(
                    invalid.Message
                ),

            _ => throw new InvalidOperationException(
                "Unsupported dataset query read result."
            )
        };
    }
}
