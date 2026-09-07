namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public abstract record ImportCsvDatasetResult
{
    public sealed record Success(
        Guid Id
    ) : ImportCsvDatasetResult;

    public sealed record ProjectNotFound
        : ImportCsvDatasetResult;

    public sealed record InvalidFile(
        string Message
    ) : ImportCsvDatasetResult;
}
