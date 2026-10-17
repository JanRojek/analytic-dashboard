namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record DatasetRowsResponse(
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Items,
    int Page,
    int PageSize,
    int TotalRows,
    int TotalPages
);
