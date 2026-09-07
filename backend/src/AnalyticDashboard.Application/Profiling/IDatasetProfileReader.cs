namespace AnalyticDashboard.Application.Profiling;

public interface IDatasetProfileReader
{
    Task<DatasetProfile> ReadProfileAsync(
        Guid datasetId,
        string name,
        string originalFileName,
        string storedPath,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken
    );
}
