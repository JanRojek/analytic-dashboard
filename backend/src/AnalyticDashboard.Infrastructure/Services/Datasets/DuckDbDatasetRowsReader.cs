using AnalyticDashboard.Application.Datasets.GetDatasetRows;
using AnalyticDashboard.Infrastructure.Services.DuckDb;

namespace AnalyticDashboard.Infrastructure.Services.Datasets;

public sealed class DuckDbDatasetRowsReader : IDatasetRowsReader
{
    private readonly DuckDbParquetExecutor _executor;

    public DuckDbDatasetRowsReader(
        DuckDbParquetExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadDatasetRowsAsync(
        string storageKey,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _executor.ExecuteAsync<IReadOnlyList<IReadOnlyDictionary<string, string?>>>(
            storageKey,
            (connection, parquetPath, ct) =>
                DuckDbRowReader.ReadAsync(
                    connection,
                    parquetPath,
                    offset,
                    limit,
                    ct
                ),
            cancellationToken
        );
    }
}
