namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public abstract record QueryDatasetResult
{
    public sealed record Success(
        IReadOnlyList<Item> Items
    ) : QueryDatasetResult;

    public sealed record NotFound
        : QueryDatasetResult;

    public sealed record InvalidQuery(
        string Message
    ) : QueryDatasetResult;

    public sealed record Item(
        string? Label,
        double Value
    );
}
