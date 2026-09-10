namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public sealed class QueryDatasetHandler
{
    private readonly DatasetQueryExecutor _queryExecutor;

    public QueryDatasetHandler(
        DatasetQueryExecutor queryExecutor)
    {
        _queryExecutor = queryExecutor;
    }

    public async Task<QueryDatasetResult> HandleAsync(
        QueryDatasetQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _queryExecutor.ExecuteAsync(
            query.DatasetId,
            query.ProjectId,
            query.OwnerId,
            query.GroupByColumn,
            query.MeasureColumn,
            query.Aggregation,
            cancellationToken
        );

        return result switch
        {
            DatasetQueryExecutionResult.Success success =>
                new QueryDatasetResult.Success(
                    success.Items
                        .Select(item =>
                            new QueryDatasetResult.Item(
                                item.Label,
                                item.Value
                            )
                        )
                        .ToList()
                ),

            DatasetQueryExecutionResult.NotFound =>
                new QueryDatasetResult.NotFound(),

            DatasetQueryExecutionResult.InvalidQuery invalid =>
                new QueryDatasetResult.InvalidQuery(
                    invalid.Message
                ),

            _ => throw new InvalidOperationException(
                "Unsupported dataset query execution result."
            )
        };
    }
}
