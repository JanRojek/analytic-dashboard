namespace AnalyticDashboard.Application.Datasets.GetDatasetById;

public sealed record GetDatasetByIdQuery(
    Guid DatasetId,
    Guid ProjectId,
    Guid OwnerId
);
