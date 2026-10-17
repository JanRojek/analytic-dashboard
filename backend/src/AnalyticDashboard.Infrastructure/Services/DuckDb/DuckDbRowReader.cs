using System.Globalization;
using DuckDB.NET.Data;

namespace AnalyticDashboard.Infrastructure.Services.DuckDb;

public static class DuckDbRowReader
{
    public static async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadAsync(
        DuckDBConnection connection,
        string parquetPath,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT *
            FROM read_parquet($parquetPath)
            LIMIT $limit
            OFFSET $offset;
        """;

        command.Parameters.AddRange(
        [
            new DuckDBParameter("parquetPath", parquetPath),
            new DuckDBParameter("limit", limit),
            new DuckDBParameter("offset", offset)
        ]);

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken
        );

        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, string?>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i)
                    ? null
                    : Convert.ToString(
                        reader.GetValue(i),
                        CultureInfo.InvariantCulture
                    );
            }

            rows.Add(row);
        }

        return rows;
    }
}
