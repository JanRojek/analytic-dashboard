using AnalyticDashboard.Application.Analytics.QueryDataset;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Infrastructure.Services.DuckDb;
using DuckDB.NET.Data;

namespace AnalyticDashboard.Infrastructure.Services.Analytics;

public sealed class DuckDbDatasetQueryReader : IDatasetQueryReader
{
    private readonly DuckDbParquetExecutor _executor;

    public DuckDbDatasetQueryReader(
        DuckDbParquetExecutor executor)
    {
        _executor = executor;
    }

    public async Task<DatasetQueryReadResult> ReadAsync(
        string storageKey,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation,
        CancellationToken cancellationToken)
    {
        return await _executor.ExecuteAsync<DatasetQueryReadResult>(
            storageKey,
            async (connection, parquetPath, ct) =>
            {
                var schema = await ReadSchemaAsync(
                    connection,
                    parquetPath,
                    ct
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
                    parquetPath,
                    groupByColumn,
                    measureColumn,
                    aggregation,
                    ct
                );

                return new DatasetQueryReadResult.Success(
                    rows
                );
            },
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

    private sealed record SchemaColumn(
        string Name,
        string DuckDbType
    );
}
