using System.Globalization;
using System.Text;

using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Services.Import;
using AnalyticDashboard.Infrastructure.Storage;

using DuckDB.NET.Data;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class DuckDbCsvCompatibilityTests
{
    private readonly ITestOutputHelper _output;

    public DuckDbCsvCompatibilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("nulls")]
    [InlineData("quotes_multiline")]
    [InlineData("unicode_bom")]
    [InlineData("types")]
    [InlineData("float_special")]
    [InlineData("duplicate_headers")]
    [InlineData("semicolon")]
    [InlineData("tab_crlf")]
    [InlineData("header_only")]
    public async Task Import_ShouldPreservePreviousSchemaValuesAndOrder(string scenario)
    {
        var (csv, rows) = CreateCsvScenario(scenario);

        await CompareAsync(
            scenario,
            Encoding.UTF8.GetBytes(csv),
            expectedRows: rows,
            threads: 1,
            rejected: false
        );
    }

    [Theory]
    [InlineData("invalid_utf8")]
    [InlineData("late_type")]
    [InlineData("late_quotes")]
    [InlineData("late_ragged")]
    public async Task Import_ShouldPreservePreviousRejection_WithoutSavingAResult(string scenario)
    {
        var bytes = scenario switch
        {
            "invalid_utf8" => new byte[] { 0xff, 0xfe, 0, 0, 0xff },
            "late_type" => Encoding.UTF8.GetBytes(CreateCsvScenario(scenario).Csv),
            _ => Encoding.UTF8.GetBytes(CreateIrregularCsv(scenario))
        };

        await CompareAsync(
            scenario,
            bytes,
            expectedRows: null,
            threads: 1,
            rejected: true
        );
    }

    [Theory]
    [InlineData("invalid_quotes")]
    [InlineData("ragged_record")]
    public async Task Import_ShouldPreservePreviousAutodetectionOfIrregularRecords(string scenario)
    {
        await CompareAsync(
            scenario,
            Encoding.UTF8.GetBytes(CreateIrregularCsv(scenario)),
            expectedRows: 1999,
            threads: 1,
            rejected: false
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Import_ShouldPreserveOrderAcrossParquetRowGroups(int threads)
    {
        const int rows = 150000;
        var csv = new StringBuilder("Id,Label,Amount\n");
        for (var index = 0; index < rows; index++)
        {
            csv.Append(index).Append(",item-").Append(index).Append(',')
                .Append(index % 1000).Append(".25\n");
        }

        await CompareAsync(
            $"multiple_groups_{threads}",
            Encoding.UTF8.GetBytes(csv.ToString()),
            expectedRows: rows,
            threads: threads,
            rejected: false
        );
    }

    private async Task CompareAsync(
        string scenario,
        byte[] bytes,
        int? expectedRows,
        int threads,
        bool rejected)
    {
        var token = TestContext.Current.CancellationToken;
        var directory = $"tests/csv-compatibility/{Guid.NewGuid():N}";
        var sourceKey = directory + "/source.csv";
        var workspace = Directory.CreateTempSubdirectory("aperture-csv-compatibility-");
        var referencePath = Path.Combine(workspace.FullName, "reference.parquet");
        var actualPath = Path.Combine(workspace.FullName, "actual.parquet");
        var storage = new RecordingStorage(new LocalFileStorage());
        var options = new ImportJobOptions
        {
            DuckDbThreads = threads,
            DuckDbMemoryLimitMb = 512
        };
        var importer = new DuckDbCsvImporter(
            storage,
            NullLogger<DuckDbCsvImporter>.Instance,
            Options.Create(options)
        );
        var previousImporter = new CtasCsvImporterReference(
            storage.Inner,
            NullLogger<CtasCsvImporterReference>.Instance,
            Options.Create(options)
        );

        try
        {
            await using (var source = new MemoryStream(bytes))
            {
                await storage.Inner.SaveAsync(sourceKey, source, token);
            }

            if (rejected)
            {
                var previousError = await Assert.ThrowsAsync<DuckDBException>(() =>
                    previousImporter.ImportAsync(sourceKey, directory + "/previous", token)
                );

                var actualError = await Assert.ThrowsAsync<DuckDBException>(() =>
                    importer.ImportAsync(sourceKey, directory + "/results", token)
                );

                Assert.Equal(previousError.ErrorType, actualError.ErrorType);
                Assert.Empty(storage.SavedKeys);
                Assert.True(await storage.ExistsAsync(sourceKey, token));
                _output.WriteLine($"{scenario}: both rejected with {actualError.ErrorType}.");
                return;
            }

            var previousMetadata = await previousImporter.ImportAsync(
                sourceKey,
                directory + "/previous",
                token
            );

            var actual = await importer.ImportAsync(
                sourceKey,
                directory + "/results",
                token
            );

            Assert.Equal(expectedRows, previousMetadata.RowCount);
            Assert.Equal(previousMetadata.RowCount, actual.RowCount);
            Assert.Equal(previousMetadata.ColumnCount, actual.ColumnCount);
            Assert.Equal(actual.StorageKey, Assert.Single(storage.SavedKeys));
            await using (var stored = await storage.OpenReadAsync(previousMetadata.StorageKey, token))
            await using (var local = File.Create(referencePath))
            {
                await stored.CopyToAsync(local, token);
            }
            await using (var stored = await storage.OpenReadAsync(actual.StorageKey, token))
            await using (var local = File.Create(actualPath))
            {
                await stored.CopyToAsync(local, token);
            }
            var schema = await AssertParquetEquivalentAsync(
                referencePath,
                actualPath,
                actual.RowCount,
                scenario.StartsWith("multiple_groups_", StringComparison.Ordinal),
                token
            );
            Assert.True(await storage.ExistsAsync(sourceKey, token));
            _output.WriteLine($"{scenario}: {actual.RowCount} rows; {schema}; values and order equal.");
        }
        finally
        {
            await storage.Inner.DeleteDirectoryAsync(directory, CancellationToken.None);
            Directory.Delete(workspace.FullName, recursive: true);
        }
    }

    private static (string Csv, int Rows) CreateCsvScenario(string scenario)
    {
        if (scenario == "late_type")
        {
            var csv = new StringBuilder("Id,Value\n");
            for (var index = 0; index < 40000; index++)
            {
                csv.Append(index).Append(',').Append(index).Append('\n');
            }
            csv.Append("40000,mixed-after-sampling\n");
            return (csv.ToString(), 40001);
        }

        return scenario switch
        {
            "nulls" => ("Id,Plain,Quoted,Whitespace,AllNull\n1,,\"\",\"  \",\n2,text,\"\", ,\n3,NULL,\"NULL\",Łódź,\n", 3),
            "quotes_multiline" => ("Id,Name,Text\n1,\"Doe, Jane\",\"He said \"\"Hi\"\"\"\n2,Bob,\"first line\nsecond line\"\n", 2),
            "unicode_bom" => ("\uFEFFId,Miasto,Tekst\n1,Łódź,你好 😀\n2,東京,e\u0301 / é\n", 2),
            "types" => ("Id,Flag,Whole,Number,OnDate,AtTime,AtTimestamp,Zoned,TextId\n" +
                "1,true,9223372036854775807,1.2345,2026-11-15,12:34:56.123456,2026-11-15T12:34:56.123456,2026-11-15T12:34:56+02:00,001\n" +
                "2,false,-9223372036854775808,-1e-10,2000-01-01,00:00:00,2000-01-01T00:00:00,2000-01-01T00:00:00Z,002\n", 2),
            "float_special" => ("Id,Value\n0,0.0\n1,-0.0\n2,NaN\n3,Inf\n4,-Inf\n5,1e308\n6,5e-324\n", 7),
            "duplicate_headers" => ("Name,Name,name,,a-b,\"quoted,header\"\na,b,c,d,e,f\n", 1),
            "semicolon" => ("Id;Value;Text\n1;\"12,5\";\"quoted;separator\"\n2;3.5;plain\n", 2),
            "tab_crlf" => ("Id\tText\tValue\r\n1\t\"a\tb\"\t10\r\n2\t\"first\r\nsecond\"\t20\r\n", 2),
            "header_only" => ("Id,Text\n", 0),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static string CreateIrregularCsv(string scenario)
    {
        var csv = new StringBuilder("Id,Text\n");
        var late = scenario.StartsWith("late_", StringComparison.Ordinal);
        var badRow = late ? 40000 : 1000;
        var rows = late ? 40001 : 2000;
        for (var index = 0; index < rows; index++)
        {
            if (index == badRow)
            {
                csv.Append(index).Append(scenario is "invalid_quotes" or "late_quotes"
                    ? ",\"unclosed,field\n"
                    : ",unexpected,third-column\n");
            }
            else
            {
                csv.Append(index).Append(",\"valid,quoted value\"\n");
            }
        }
        return csv.ToString();
    }

    private static async Task<string> AssertParquetEquivalentAsync(
        string previousPath,
        string actualPath,
        int expectedRows,
        bool requireMultipleGroups,
        CancellationToken cancellationToken)
    {
        await using var previous = new DuckDBConnection("DataSource=:memory:;threads=1;memory_limit=512MiB");
        await using var actual = new DuckDBConnection("DataSource=:memory:;threads=1;memory_limit=512MiB");
        await previous.OpenAsync(cancellationToken);
        await actual.OpenAsync(cancellationToken);
        await using var previousCommand = previous.CreateCommand();
        await using var actualCommand = actual.CreateCommand();
        previousCommand.CommandText = "SELECT * FROM read_parquet($path)";
        previousCommand.Parameters.Add(new DuckDBParameter("path", previousPath));
        actualCommand.Parameters.Add(new DuckDBParameter("path", actualPath));
        if (requireMultipleGroups)
        {
            actualCommand.CommandText = "SELECT num_row_groups FROM parquet_file_metadata($path)";
            var groups = Convert.ToInt64(await actualCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            Assert.True(groups > 1);
        }
        actualCommand.CommandText = previousCommand.CommandText;
        await using var previousReader = await previousCommand.ExecuteReaderAsync(cancellationToken);
        await using var actualReader = await actualCommand.ExecuteReaderAsync(cancellationToken);
        Assert.Equal(previousReader.FieldCount, actualReader.FieldCount);
        var schema = new List<string>();
        for (var column = 0; column < previousReader.FieldCount; column++)
        {
            Assert.Equal(previousReader.GetName(column), actualReader.GetName(column));
            Assert.Equal(previousReader.GetDataTypeName(column), actualReader.GetDataTypeName(column));
            Assert.Equal(previousReader.GetFieldType(column), actualReader.GetFieldType(column));
            schema.Add(previousReader.GetName(column) + ":" + previousReader.GetDataTypeName(column));
        }
        var rows = 0;
        while (await previousReader.ReadAsync(cancellationToken))
        {
            Assert.True(await actualReader.ReadAsync(cancellationToken));
            for (var column = 0; column < previousReader.FieldCount; column++)
            {
                Assert.Equal(
                    Normalize(previousReader.GetValue(column)),
                    Normalize(actualReader.GetValue(column))
                );
            }
            rows++;
        }
        Assert.False(await actualReader.ReadAsync(cancellationToken));
        Assert.Equal(expectedRows, rows);
        return string.Join("|", schema);
    }

    private static string? Normalize(object value)
    {
        return value switch
        {
            DBNull => null,
            double number => BitConverter.DoubleToInt64Bits(number).ToString("X16", CultureInfo.InvariantCulture),
            float number => BitConverter.SingleToInt32Bits(number).ToString("X8", CultureInfo.InvariantCulture),
            DateTime date => $"{date.Ticks}:{date.Kind}",
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.DayNumber.ToString(CultureInfo.InvariantCulture),
            TimeOnly time => time.Ticks.ToString(CultureInfo.InvariantCulture),
            TimeSpan time => time.Ticks.ToString(CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public IFileStorage Inner { get; }
        public List<string> SavedKeys { get; } = [];

        public RecordingStorage(IFileStorage inner)
        {
            Inner = inner;
        }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            SavedKeys.Add(storageKey);
            await Inner.SaveAsync(storageKey, content, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Inner.OpenReadAsync(storageKey, cancellationToken);

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            Inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            Inner.DeleteAsync(storageKey, cancellationToken);

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            Inner.DeleteDirectoryAsync(storageKey, cancellationToken);
    }
}
