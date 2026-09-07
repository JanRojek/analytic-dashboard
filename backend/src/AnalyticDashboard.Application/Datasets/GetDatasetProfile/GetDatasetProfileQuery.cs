namespace AnalyticDashboard.Application.Datasets.GetDatasetProfile;

public sealed record GetDatasetProfileQuery(
    Guid DatasetId,
    Guid ProjectId,
    Guid OwnerId
);
