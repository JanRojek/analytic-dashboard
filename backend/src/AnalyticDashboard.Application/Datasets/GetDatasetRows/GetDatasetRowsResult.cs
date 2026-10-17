namespace AnalyticDashboard.Application.Datasets.GetDatasetRows;

public abstract record GetDatasetRowsResult
{
    public sealed record Success(
        IReadOnlyList<IReadOnlyDictionary<string, string?>> Items,
        int Page,
        int PageSize,
        int TotalRows,
        int TotalPages
    ) : GetDatasetRowsResult;

    public sealed record NotFound
        : GetDatasetRowsResult;

    public sealed record InvalidPageSize(
        string Message
    ) : GetDatasetRowsResult;
}
