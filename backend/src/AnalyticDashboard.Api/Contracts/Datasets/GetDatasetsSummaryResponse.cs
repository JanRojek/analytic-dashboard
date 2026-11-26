namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record GetDatasetsSummaryResponse(
    int TotalCount,
    int ReadyCount,
    long TotalRows
);
