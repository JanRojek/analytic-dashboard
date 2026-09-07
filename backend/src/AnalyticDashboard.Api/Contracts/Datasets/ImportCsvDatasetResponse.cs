namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record ImportCsvDatasetResponse(
    Guid DatasetId,
    Guid DatasetVersionId,
    Guid ImportJobId
);
