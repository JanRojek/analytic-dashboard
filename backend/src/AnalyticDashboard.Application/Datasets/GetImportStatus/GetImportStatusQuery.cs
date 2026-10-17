namespace AnalyticDashboard.Application.Datasets.GetImportStatus;

public sealed record GetImportStatusQuery(
    Guid DatasetId,
    Guid ProjectId,
    Guid OwnerId
);
