namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record QueryDatasetResponse(
    IReadOnlyList<QueryDatasetItemResponse> Items
);

public sealed record QueryDatasetItemResponse(
    string? Label,
    double Value
);
