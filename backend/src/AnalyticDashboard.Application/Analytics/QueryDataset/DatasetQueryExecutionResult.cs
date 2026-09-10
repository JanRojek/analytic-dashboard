namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public abstract record DatasetQueryExecutionResult
{
    public sealed record Success(
        IReadOnlyList<Item> Items
    ) : DatasetQueryExecutionResult;

    public sealed record NotFound : DatasetQueryExecutionResult;

    public sealed record InvalidQuery(
        string Message
    ) : DatasetQueryExecutionResult;

    public sealed record Item(
        string? Label,
        double Value
    );
}
