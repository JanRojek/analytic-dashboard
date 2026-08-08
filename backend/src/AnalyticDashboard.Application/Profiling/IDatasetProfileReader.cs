using AnalyticDashboard.Application.Datasets.GetDatasetProfile;

namespace AnalyticDashboard.Application.Profiling;

public interface IDatasetProfileReader
{
    Task<GetDatasetProfileResponse> ReadProfileAsync(
        Guid datasetId,
        string name,
        string originalFileName,
        string storedPath,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken);
}