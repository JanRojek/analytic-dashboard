using AnalyticDashboard.Infrastructure.Storage;
using DuckDB.NET.Data;

namespace AnalyticDashboard.Infrastructure.Services.DuckDb;

public sealed class DuckDbParquetExecutor
{
    private readonly LocalFileStorage _fileStorage;

    public DuckDbParquetExecutor(LocalFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    public Task<TResult> ExecuteAsync<TResult>(
        string storageKey,
        Func<DuckDBConnection, string, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        return _fileStorage.ExecuteWithReadLeaseAsync(
            storageKey,
            async (parquetPath, ct) =>
            {
                await using var connection = new DuckDBConnection("DataSource=:memory:");
                await connection.OpenAsync(ct);

                return await operation(connection, parquetPath, ct);
            },
            cancellationToken
        );
    }
}
