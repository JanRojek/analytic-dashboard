using AnalyticDashboard.Application.Analytics.QueryDataset;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Analytics;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Analytics;

public sealed class DuckDbDatasetQueryReader : IDatasetQueryReader
{
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DuckDbDatasetQueryReader> _logger;

    public DuckDbDatasetQueryReader(
        IFileStorage fileStorage,
        ILogger<DuckDbDatasetQueryReader> logger)
    {
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<DatasetQueryReadResult> ReadAsync(
        string storageKey,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation,
        CancellationToken cancellationToken)
    {
        var tempParquetPath = Path.Combine(
            Path.GetTempPath(),
            $"analytic-dashboard-query-{Guid.NewGuid():N}.parquet"
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

            var schema = await ReadSchemaAsync(
                connection,
                tempParquetPath,
                cancellationToken
            );

            var groupBy = schema.SingleOrDefault(
                column => column.Name == groupByColumn
            );

            if (groupBy is null)
            {
                return new DatasetQueryReadResult.InvalidQuery(
                    $"Column '{groupByColumn}' does not exist."
                );
            }

            var measure = schema.SingleOrDefault(
                column => column.Name == measureColumn
            );

            if (measure is null)
            {
                return new DatasetQueryReadResult.InvalidQuery(
                    $"Column '{measureColumn}' does not exist."
                );
            }

            if (aggregation != AggregationType.Count
                && !IsNumericType(measure.DuckDbType))
            {
                return new DatasetQueryReadResult.InvalidQuery(
                    $"Column '{measureColumn}' is not numeric."
                );
            }

            var rows = await ExecuteAggregationAsync(
                connection,
                tempParquetPath,
                groupByColumn,
                measureColumn,
                aggregation,
                cancellationToken
            );

            return new DatasetQueryReadResult.Success(
                rows
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

    private static async Task<IReadOnlyList<SchemaColumn>> ReadSchemaAsync(
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

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken
        );

        var columns = new List<SchemaColumn>();

        while (await reader.ReadAsync(cancellationToken))
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

    private static async Task<IReadOnlyList<DatasetQueryReadResult.Row>> ExecuteAggregationAsync(
        DuckDBConnection connection,
        string parquetPath,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation,
        CancellationToken cancellationToken)
    {
        var quotedGroupBy = QuoteIdentifier(groupByColumn);

        var quotedMeasure = QuoteIdentifier(measureColumn);

        var aggregationExpression = aggregation switch
        {
            AggregationType.Sum => $"SUM({quotedMeasure})",

            AggregationType.Average => $"AVG({quotedMeasure})",

            AggregationType.Min => $"MIN({quotedMeasure})",

            AggregationType.Max => $"MAX({quotedMeasure})",

            AggregationType.Count => $"COUNT({quotedMeasure})",

            _ => throw new InvalidOperationException(
                "Unsupported aggregation type."
            )
        };

        var havingClause = aggregation == AggregationType.Count
            ? string.Empty
            : $"HAVING {aggregationExpression} IS NOT NULL";

        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT
                CAST({quotedGroupBy} AS VARCHAR) AS label,
                CAST({aggregationExpression} AS DOUBLE) AS value
            FROM read_parquet($parquetPath)
            GROUP BY {quotedGroupBy}
            {havingClause}
            ORDER BY label NULLS LAST;
            """;

        command.Parameters.Add(
            new DuckDBParameter(
                "parquetPath",
                parquetPath
            )
        );

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken
        );

        var rows = new List<DatasetQueryReadResult.Row>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var label = reader.IsDBNull(0)
                ? null
                : reader.GetString(0);

            var value = reader.GetDouble(1);

            rows.Add(
                new DatasetQueryReadResult.Row(
                    label,
                    value
                )
            );
        }

        return rows;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static bool IsNumericType(string duckDbType)
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

    private void TryDeleteTempFile(string path)
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
                "Failed to delete temporary analytics file {FilePath}.",
                path
            );
        }
    }

    private sealed record SchemaColumn(
        string Name,
        string DuckDbType
    );
}
