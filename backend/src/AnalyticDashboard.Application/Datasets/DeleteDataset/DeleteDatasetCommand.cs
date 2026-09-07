namespace AnalyticDashboard.Application.Datasets.DeleteDataset;

public sealed record DeleteDatasetCommand(
    Guid DatasetId,
    Guid ProjectId,
    Guid OwnerId
);
