namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public sealed record GetDatasetsQuery(
    Guid ProjectId,
    Guid OwnerId
);
