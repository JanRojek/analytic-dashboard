namespace AnalyticDashboard.Application.Profiling;

public interface IDatasetProfileReader
{
    Task<DatasetProfile> ReadProfileAsync(
        Guid datasetId,
        string name,
        string originalFileName,
        string storageKey,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken
    );
}
