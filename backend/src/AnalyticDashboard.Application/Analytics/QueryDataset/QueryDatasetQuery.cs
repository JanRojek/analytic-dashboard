using AnalyticDashboard.Domain.Analytics;

namespace AnalyticDashboard.Application.Analytics.QueryDataset;

public sealed record QueryDatasetQuery(
    Guid DatasetId,
    Guid ProjectId,
    Guid OwnerId,
    string GroupByColumn,
    string MeasureColumn,
    AggregationType Aggregation
);
