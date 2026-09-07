using System.Globalization;
using AnalyticDashboard.Application.Profiling;
using AnalyticDashboard.Application.Storage;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Profiling;

public sealed class DuckDbDatasetProfileReader
    : IDatasetProfileReader
{
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DuckDbDatasetProfileReader> _logger;

    public DuckDbDatasetProfileReader(
        IFileStorage fileStorage,
        ILogger<DuckDbDatasetProfileReader> logger)
    {
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<DatasetProfile> ReadProfileAsync(
        Guid datasetId,
        string name,
        string originalFileName,
        string storageKey,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken)
    {
        var tempParquetPath = Path.Combine(
            Path.GetTempPath(),
            $"analytic-dashboard-profile-{Guid.NewGuid():N}.parquet"
        );

        try
        {
            await CopyToTempAsync(
                storageKey,
                tempParquetPath,
                cancellationToken
            );

            await using var connection = new DuckDBConnection(
                "DataSource=:memory:"
            );

            await connection.OpenAsync(
                cancellationToken
            );

            var columns = await ReadColumnProfilesAsync(
                connection,
                tempParquetPath,
                cancellationToken
            );

            var preview = await ReadPreviewAsync(
                connection,
                tempParquetPath,
                cancellationToken
            );

            return new DatasetProfile(
                datasetId,
                name,
                originalFileName,
                rowCount,
                columnCount,
                columns,
                preview
            );
        }
        finally
        {
            TryDeleteTempFile(
                tempParquetPath
            );
        }
    }

    private async Task CopyToTempAsync(
        string storageKey,
        string tempPath,
        CancellationToken cancellationToken)
    {
        await using var source =
            await _fileStorage.OpenReadAsync(
                storageKey,
                cancellationToken
            );

        await using var destination = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous
        );

        await source.CopyToAsync(
            destination,
            cancellationToken
        );
    }

    private static async Task<IReadOnlyList<ColumnProfile>>
        ReadColumnProfilesAsync(
            DuckDBConnection connection,
            string parquetPath,
            CancellationToken cancellationToken)
    {
        var schema = await ReadSchemaAsync(
            connection,
            parquetPath,
            cancellationToken
        );

        var profiles = new List<ColumnProfile>(
            schema.Count
        );

        foreach (var column in schema)
        {
            var profile = await ReadColumnProfileAsync(
                connection,
                parquetPath,
                column,
                cancellationToken
            );

            profiles.Add(profile);
        }

        return profiles;
    }

    private static async Task<IReadOnlyList<SchemaColumn>>
        ReadSchemaAsync(
            DuckDBConnection connection,
            string parquetPath,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            DESCRIBE
            SELECT *
            FROM read_parquet($parquetPath);
            """;

        command.Parameters.Add(
            new DuckDBParameter(
                "parquetPath",
                parquetPath
            )
        );

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken
            );

        var columns = new List<SchemaColumn>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            columns.Add(
                new SchemaColumn(
                    reader.GetString(0),
                    reader.GetString(1)
                )
            );
        }

        return columns;
    }

    private static async Task<ColumnProfile>
        ReadColumnProfileAsync(
            DuckDBConnection connection,
            string parquetPath,
            SchemaColumn column,
            CancellationToken cancellationToken)
    {
        var quotedColumn = QuoteIdentifier(
            column.Name
        );

        if (IsNumericType(column.DuckDbType))
        {
            await using var command =
                connection.CreateCommand();

            command.CommandText = $"""
                SELECT
                    COUNT(*) FILTER (
                        WHERE {quotedColumn} IS NULL
                    ),
                    MIN({quotedColumn}),
                    MAX({quotedColumn}),
                    AVG({quotedColumn})
                FROM read_parquet($parquetPath);
                """;

            command.Parameters.Add(
                new DuckDBParameter(
                    "parquetPath",
                    parquetPath
                )
            );

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken
                );

            await reader.ReadAsync(
                cancellationToken
            );

            return new ColumnProfile(
                column.Name,
                "number",
                Convert.ToInt32(
                    reader.GetValue(0),
                    CultureInfo.InvariantCulture
                ),
                ToInvariantString(reader.GetValue(1)),
                ToInvariantString(reader.GetValue(2)),
                reader.IsDBNull(3)
                    ? null
                    : Math.Round(
                        Convert.ToDouble(
                            reader.GetValue(3),
                            CultureInfo.InvariantCulture
                        ),
                        2
                    )
            );
        }

        var nullCount = await ReadNullCountAsync(
            connection,
            parquetPath,
            quotedColumn,
            cancellationToken
        );

        if (IsDateType(column.DuckDbType))
        {
            return new ColumnProfile(
                column.Name,
                "date",
                nullCount,
                null,
                null,
                null
            );
        }

        return new ColumnProfile(
            column.Name,
            "text",
            nullCount,
            null,
            null,
            null
        );
    }

    private static async Task<int> ReadNullCountAsync(
        DuckDBConnection connection,
        string parquetPath,
        string quotedColumn,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = $"""
            SELECT COUNT(*)
            FROM read_parquet($parquetPath)
            WHERE {quotedColumn} IS NULL;
            """;

        command.Parameters.Add(
            new DuckDBParameter(
                "parquetPath",
                parquetPath
            )
        );

        var value = await command.ExecuteScalarAsync(
            cancellationToken
        );

        return Convert.ToInt32(
            value,
            CultureInfo.InvariantCulture
        );
    }

    private static async Task<
        IReadOnlyList<IReadOnlyDictionary<string, string?>>>
        ReadPreviewAsync(
            DuckDBConnection connection,
            string parquetPath,
            CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText = """
            SELECT *
            FROM read_parquet($parquetPath)
            LIMIT 10;
            """;

        command.Parameters.Add(
            new DuckDBParameter(
                "parquetPath",
                parquetPath
            )
        );

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken
            );

        var rows =
            new List<IReadOnlyDictionary<string, string?>>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var row =
                new Dictionary<string, string?>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] =
                    reader.IsDBNull(i)
                        ? null
                        : ToInvariantString(
                            reader.GetValue(i)
                        );
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string QuoteIdentifier(
        string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static bool IsNumericType(
        string duckDbType)
    {
        var type = duckDbType.ToUpperInvariant();

        return type.StartsWith("DECIMAL")
               || type is
                   "TINYINT"
                   or "SMALLINT"
                   or "INTEGER"
                   or "BIGINT"
                   or "HUGEINT"
                   or "UTINYINT"
                   or "USMALLINT"
                   or "UINTEGER"
                   or "UBIGINT"
                   or "UHUGEINT"
                   or "FLOAT"
                   or "DOUBLE";
    }

    private static bool IsDateType(
        string duckDbType)
    {
        var type = duckDbType.ToUpperInvariant();

        return type == "DATE"
               || type.StartsWith("TIMESTAMP");
    }

    private static string? ToInvariantString(
        object value)
    {
        if (value is DBNull)
        {
            return null;
        }

        return value is IFormattable formattable
            ? formattable.ToString(
                null,
                CultureInfo.InvariantCulture
            )
            : value.ToString();
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
                "Failed to delete temporary profile file {FilePath}.",
                path
            );
        }
    }

    private sealed record SchemaColumn(
        string Name,
        string DuckDbType
    );
}
