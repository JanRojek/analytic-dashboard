namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record QueryDatasetRequest(
    string GroupByColumn,
    string MeasureColumn,
    string Aggregation
);
