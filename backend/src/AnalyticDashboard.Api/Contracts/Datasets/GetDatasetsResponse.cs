namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record GetDatasetsResponse(
    IReadOnlyList<DatasetResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);
