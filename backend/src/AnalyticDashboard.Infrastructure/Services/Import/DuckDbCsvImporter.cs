using System.Globalization;
using AnalyticDashboard.Application.Storage;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class DuckDbCsvImporter
{
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DuckDbCsvImporter> _logger;

    public DuckDbCsvImporter(
        IFileStorage fileStorage,
        ILogger<DuckDbCsvImporter> logger)
    {
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<DuckDbCsvImportResult> ImportAsync(
        string sourceStorageKey,
        string resultStorageKey,
        CancellationToken cancellationToken)
    {
        var tempSourcePath = Path.Combine(
            Path.GetTempPath(),
            $"analytic-dashboard-{Guid.NewGuid():N}.csv"
        );

        var tempParquetPath = Path.Combine(
            Path.GetTempPath(),
            $"analytic-dashboard-{Guid.NewGuid():N}.parquet"
        );

        try
        {
            await CopySourceToTempAsync(
                sourceStorageKey,
                tempSourcePath,
                cancellationToken
            );

            var result = await ConvertToParquetAsync(
                tempSourcePath,
                tempParquetPath,
                cancellationToken
            );

            await SaveParquetAsync(
                tempParquetPath,
                resultStorageKey,
                cancellationToken
            );

            return new DuckDbCsvImportResult(
                resultStorageKey,
                result.RowCount,
                result.ColumnCount
            );
        }
        catch
        {
            try
            {
                await _fileStorage.DeleteAsync(
                    resultStorageKey,
                    CancellationToken.None
                );
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Failed to clean up result storage object {StorageKey} after DuckDB import failure.",
                    resultStorageKey
                );
            }

            throw;
        }
        finally
        {
            TryDeleteTempFile(
                tempSourcePath
            );

            TryDeleteTempFile(
                tempParquetPath
            );
        }
    }

    private async Task CopySourceToTempAsync(
        string sourceStorageKey,
        string tempSourcePath,
        CancellationToken cancellationToken)
    {
        await using var sourceStream =
            await _fileStorage.OpenReadAsync(
                sourceStorageKey,
                cancellationToken
            );

        await using var tempStream = new FileStream(
            tempSourcePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous
        );

        await sourceStream.CopyToAsync(
            tempStream,
            cancellationToken
        );
    }

    private static async Task<ImportMetadata> ConvertToParquetAsync(
        string sourcePath,
        string parquetPath,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new DuckDBConnection(
                "DataSource=:memory:"
            );

        await connection.OpenAsync(
            cancellationToken
        );

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE imported_dataset AS
            SELECT *
            FROM read_csv_auto(
                $sourcePath,
                header = true
            );
            """;

        command.Parameters.Add(
            new DuckDBParameter(
                "sourcePath",
                sourcePath
            )
        );

        await command.ExecuteNonQueryAsync(
            cancellationToken
        );

        command.Parameters.Clear();

        command.CommandText = """
            SELECT COUNT(*)
            FROM imported_dataset;
            """;

        var rowCountValue =
            await command.ExecuteScalarAsync(
                cancellationToken
            );

        var rowCount = Convert.ToInt32(
            rowCountValue,
            CultureInfo.InvariantCulture
        );

        command.CommandText = """
            SELECT *
            FROM imported_dataset
            LIMIT 0;
            """;

        int columnCount;

        await using (var reader = await command.ExecuteReaderAsync(
            cancellationToken
        ))
        {
            columnCount = reader.FieldCount;
        }

        command.CommandText = """
            COPY imported_dataset
            TO $parquetPath
            (
                FORMAT parquet,
                COMPRESSION zstd
            );
            """;

        command.Parameters.Add(
            new DuckDBParameter(
                "parquetPath",
                parquetPath
            )
        );

        await command.ExecuteNonQueryAsync(
            cancellationToken
        );

        return new ImportMetadata(
            rowCount,
            columnCount
        );
    }

    private async Task SaveParquetAsync(
        string parquetPath,
        string resultStorageKey,
        CancellationToken cancellationToken)
    {
        await using var parquetStream = new FileStream(
            parquetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous
                | FileOptions.SequentialScan
        );

        await _fileStorage.SaveAsync(
            resultStorageKey,
            parquetStream,
            cancellationToken
        );
    }

    private void TryDeleteTempFile(
        string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to delete temporary DuckDB import file {FilePath}.",
                filePath
            );
        }
    }

    private sealed record ImportMetadata(
        int RowCount,
        int ColumnCount
    );
}
