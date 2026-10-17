namespace AnalyticDashboard.Application.Datasets.GetDatasetRows;

public interface IDatasetRowsReader
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadDatasetRowsAsync(
        string storageKey,
        int offset,
        int limit,
        CancellationToken cancellationToken
    );
}
