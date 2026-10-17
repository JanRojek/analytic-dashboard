using AnalyticDashboard.Application.Storage;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.DuckDb;

public sealed class DuckDbParquetExecutor
{
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DuckDbParquetExecutor> _logger;

    public DuckDbParquetExecutor(
        IFileStorage fileStorage,
        ILogger<DuckDbParquetExecutor> logger)
    {
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        string storageKey,
        Func<
            DuckDBConnection,
            string,
            CancellationToken,
            Task<TResult>
        > operation,
        CancellationToken cancellationToken)
    {
        var tempParquetPath = Path.Combine(
            Path.GetTempPath(),
            $"analytic-dashboard-{Guid.NewGuid():N}.parquet"
        );

        try
        {
            await using (var source = await _fileStorage.OpenReadAsync(
                storageKey,
                cancellationToken))
            await using (var destination = new FileStream(
                tempParquetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous))
            {
                await source.CopyToAsync(
                    destination,
                    cancellationToken
                );
            }

            await using var connection = new DuckDBConnection(
                "DataSource=:memory:"
            );

            await connection.OpenAsync(
                cancellationToken
            );

            return await operation(
                connection,
                tempParquetPath,
                cancellationToken
            );
        }
        finally
        {
            TryDeleteTempFile(tempParquetPath);
        }
    }

    private void TryDeleteTempFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to delete temporary DuckDB parquet file {FilePath}.",
                path
            );
        }
    }
}
