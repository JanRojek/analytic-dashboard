namespace AnalyticDashboard.Application.Datasets.GetDatasetsSummary;

public sealed record GetDatasetsSummaryQuery(
    Guid ProjectId,
    Guid OwnerId
);
