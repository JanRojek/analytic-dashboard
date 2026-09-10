using AnalyticDashboard.Domain.Analytics;

namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public interface IDatasetQueryReader
{
    Task<DatasetQueryReadResult> ReadAsync(
        string storageKey,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation,
        CancellationToken cancellationToken
    );
}

public abstract record DatasetQueryReadResult
{
    public sealed record Success(
        IReadOnlyList<Row> Rows
    ) : DatasetQueryReadResult;

    public sealed record InvalidQuery(
        string Message
    ) : DatasetQueryReadResult;

    public sealed record Row(
        string? Label,
        double Value
    );
}
