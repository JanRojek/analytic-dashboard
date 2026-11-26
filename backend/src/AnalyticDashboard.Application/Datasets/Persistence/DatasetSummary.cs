namespace AnalyticDashboard.Application.Datasets.Persistence;

public sealed record DatasetSummary(
    int TotalCount,
    int ReadyCount,
    long TotalRows
);
