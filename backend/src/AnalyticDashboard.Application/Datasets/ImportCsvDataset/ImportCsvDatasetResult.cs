namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public abstract record ImportCsvDatasetResult
{
    public sealed record Accepted(
        Guid DatasetId,
        Guid DatasetVersionId,
        Guid ImportJobId
    ) : ImportCsvDatasetResult;

    public sealed record ProjectNotFound
        : ImportCsvDatasetResult;

    public sealed record InvalidFile(
        string Message
    ) : ImportCsvDatasetResult;
}
