using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AnalyticDashboard.Infrastructure.Services.Import;
using AnalyticDashboard.Infrastructure.Storage;

using DuckDB.NET.Data;
using DuckDB.NET.Native;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Performance;

public sealed class CsvImportPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public CsvImportPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Performance")]
    public async Task Import_ShouldBeMeasuredWithLargeSources_SequentiallyAndInParallel()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        var options = new ImportJobOptions();
        var storage = new LocalFileStorage();
        var rootKey = $"tests/import-performance/{Guid.NewGuid():N}";
        var storageRoot = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "storage",
            rootKey.Replace('/', Path.DirectorySeparatorChar)
        ));
        var sourceWorkspace = Directory.CreateTempSubdirectory("aperture-csv-benchmark-");
        var reports = new List<object>();

        try
        {
            object? nativeVersion;
            await using (var connection = new DuckDBConnection("DataSource=:memory:;threads=1"))
            {
                await connection.OpenAsync(token);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT version()";
                nativeVersion = await command.ExecuteScalarAsync(token);
            }

            var warmupKey = rootKey + "/warmup.csv";
            await using (var warmup = new MemoryStream("Category,Value\nWarmup,1\n"u8.ToArray()))
            {
                await storage.SaveAsync(warmupKey, warmup, token);
            }
            var warmupImporter = new DuckDbCsvImporter(
                storage,
                NullLogger<DuckDbCsvImporter>.Instance,
                Options.Create(options)
            );
            await warmupImporter.ImportAsync(warmupKey, rootKey + "/warmup", token);
            await storage.DeleteAsync(warmupKey, token);
            await storage.DeleteDirectoryAsync(rootKey + "/warmup", token);

            foreach (var shape in new[]
            {
                (Name: "tall_numeric", Rows: 1000000, Columns: 5, AllowBudgetFailure: false),
                (Name: "wide_text", Rows: 100000, Columns: 4, AllowBudgetFailure: false),
                (Name: "wide_text_stress", Rows: 150000, Columns: 4, AllowBudgetFailure: true)
            })
            {
                var sourcePath = Path.Combine(sourceWorkspace.FullName, shape.Name + ".csv");
                await WriteSourceAsync(sourcePath, shape.Name, shape.Rows, token);
                var sourceBytes = new FileInfo(sourcePath).Length;
                var sourceKey = rootKey + "/" + shape.Name + ".csv";
                await using (var source = File.OpenRead(sourcePath))
                {
                    await storage.SaveAsync(sourceKey, source, token);
                }
                File.Delete(sourcePath);

                foreach (var concurrency in new[] { 1, 2 })
                {
                    var samples = new List<object>();
                    for (var iteration = 0; iteration < 3; iteration++)
                    {
                        var runKey = $"{rootKey}/runs/{shape.Name}-{concurrency}-{iteration}";
                        var sample = await MeasureAsync(
                            sourceKey,
                            runKey,
                            storageRoot,
                            sourceBytes,
                            shape.Rows,
                            shape.Columns,
                            concurrency,
                            shape.AllowBudgetFailure,
                            options,
                            token
                        );
                        samples.Add(sample);
                        await storage.DeleteDirectoryAsync(runKey, token);
                    }
                    reports.Add(new
                    {
                        Shape = shape.Name,
                        RowsPerImport = shape.Rows,
                        SourceBytesPerImport = sourceBytes,
                        Concurrency = concurrency,
                        Samples = samples
                    });
                }
                await storage.DeleteAsync(sourceKey, token);
            }

            var report = JsonSerializer.Serialize(new
            {
                Runtime = Environment.Version.ToString(),
                NativeDuckDbVersion = nativeVersion,
                LogicalProcessors = Environment.ProcessorCount,
                Options = options,
                SamplingIntervalMs = 50,
                Scenarios = reports
            }, new JsonSerializerOptions { WriteIndented = true });
            _output.WriteLine(report);
            var reportPath = Environment.GetEnvironmentVariable("APERTURE_CSV_IMPORT_REPORT");
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                await File.WriteAllTextAsync(reportPath, report, token);
            }
        }
        finally
        {
            await storage.DeleteDirectoryAsync(rootKey, CancellationToken.None);
            Directory.Delete(sourceWorkspace.FullName, recursive: true);
        }
    }

    private static async Task WriteSourceAsync(
        string path,
        string shape,
        int rows,
        CancellationToken cancellationToken)
    {
        await using var file = File.Create(path);
        await using var writer = new StreamWriter(file, Encoding.UTF8, 65536);
        await writer.WriteLineAsync(
            (shape.StartsWith("wide_text", StringComparison.Ordinal)
                ? "Id,Category,Payload,Value" : "Id,Category,Value,OccurredAt,Code").AsMemory(),
            cancellationToken
        );
        for (var index = 0; index < rows; index++)
        {
            string line;
            if (shape.StartsWith("wide_text", StringComparison.Ordinal))
            {
                var hash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(index)));
                var payload = string.Concat(Enumerable.Repeat(hash, 16));
                line = $"{index},Group-{index % 128},{payload},{index % 10000}.25";
            }
            else
            {
                line = $"{index},Group-{index % 128},{index % 10000}.25,2026-01-01T12:34:56,code-{index % 1024}";
            }
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
    }

    private static async Task<object> MeasureAsync(
        string sourceKey,
        string runKey,
        string storageRoot,
        long sourceBytes,
        int expectedRows,
        int expectedColumns,
        int concurrency,
        bool allowBudgetFailure,
        ImportJobOptions options,
        CancellationToken cancellationToken)
    {
        var existingWorkspaces = Directory.GetDirectories(
            Path.GetTempPath(),
            "analytic-dashboard-import-*"
        ).ToHashSet(StringComparer.Ordinal);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baselineWorkingSet = process.WorkingSet64;
        var baselinePrivateBytes = process.PrivateMemorySize64;
        var baselineCpu = process.TotalProcessorTime;
        var peakWorkingSet = baselineWorkingSet;
        var peakPrivateBytes = baselinePrivateBytes;
        long peakWorkspaceBytes = 0;
        long peakSpillBytes = 0;
        long peakStoredBytes = 0;
        var observations = 0;
        var incompleteDiskReads = 0;
        var lastSampleAt = Stopwatch.GetTimestamp();
        double maxSampleGapMs = 0;
        using var samplingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void Sample()
        {
            var now = Stopwatch.GetTimestamp();
            maxSampleGapMs = Math.Max(maxSampleGapMs, Stopwatch.GetElapsedTime(lastSampleAt, now).TotalMilliseconds);
            lastSampleAt = now;
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64);
            long workspaceBytes = 0;
            long spillBytes = 0;
            foreach (var workspace in Directory.GetDirectories(Path.GetTempPath(), "analytic-dashboard-import-*"))
            {
                if (existingWorkspaces.Contains(workspace))
                {
                    continue;
                }
                workspaceBytes += DirectoryBytes(workspace, ref incompleteDiskReads);
                spillBytes += DirectoryBytes(Path.Combine(workspace, "spill"), ref incompleteDiskReads);
            }
            peakWorkspaceBytes = Math.Max(peakWorkspaceBytes, workspaceBytes);
            peakSpillBytes = Math.Max(peakSpillBytes, spillBytes);
            peakStoredBytes = Math.Max(peakStoredBytes, DirectoryBytes(storageRoot, ref incompleteDiskReads));
            observations++;
        }

        var sampler = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            try
            {
                do
                {
                    Sample();
                }
                while (await timer.WaitForNextTickAsync(samplingCancellation.Token));
            }
            catch (OperationCanceledException) when (samplingCancellation.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);
        var elapsed = Stopwatch.StartNew();
        (DuckDbCsvImportResult? Result, DuckDBErrorType? Error)[] outcomes;
        try
        {
            var conversions = Enumerable.Range(0, concurrency).Select(index => Task.Run(async () =>
            {
                var importer = new DuckDbCsvImporter(
                    new LocalFileStorage(),
                    NullLogger<DuckDbCsvImporter>.Instance,
                    Options.Create(options)
                );
                try
                {
                    var result = await importer.ImportAsync(
                        sourceKey,
                        $"{runKey}/{index}",
                        cancellationToken
                    );
                    return (Result: (DuckDbCsvImportResult?)result, Error: (DuckDBErrorType?)null);
                }
                catch (DuckDBException exception) when (
                    allowBudgetFailure && exception.ErrorType == DuckDBErrorType.OutOfMemory)
                {
                    return (Result: (DuckDbCsvImportResult?)null, Error: (DuckDBErrorType?)exception.ErrorType);
                }
            }, cancellationToken)).ToArray();
            outcomes = await Task.WhenAll(conversions);
            elapsed.Stop();
            Assert.All(outcomes.Where(item => item.Result is not null), item =>
            {
                Assert.Equal(expectedRows, item.Result!.RowCount);
                Assert.Equal(expectedColumns, item.Result.ColumnCount);
            });
        }
        finally
        {
            await samplingCancellation.CancelAsync();
            await sampler;
        }

        Sample();
        Assert.DoesNotContain(
            Directory.GetDirectories(Path.GetTempPath(), "analytic-dashboard-import-*"),
            workspace => !existingWorkspaces.Contains(workspace)
        );
        var results = outcomes.Where(item => item.Result is not null).Select(item => item.Result!).ToArray();
        Assert.Equal(results.Length, results.Select(item => item.StorageKey).Distinct().Count());
        if (!allowBudgetFailure)
        {
            Assert.Equal(concurrency, results.Length);
        }
        var outputBytes = results.Sum(result => new FileInfo(Path.Combine(
            Directory.GetCurrentDirectory(),
            "storage",
            result.StorageKey.Replace('/', Path.DirectorySeparatorChar)
        )).Length);
        return new
        {
            ElapsedMs = elapsed.Elapsed.TotalMilliseconds,
            CpuMs = (process.TotalProcessorTime - baselineCpu).TotalMilliseconds,
            SuccessfulInputMiBPerSecond = sourceBytes * results.Length / 1048576d / elapsed.Elapsed.TotalSeconds,
            Outcomes = outcomes.Select(item => new
            {
                Status = item.Result is null ? "resource_limit" : "completed",
                ErrorType = item.Error?.ToString()
            }),
            BaselineWorkingSetBytes = baselineWorkingSet,
            PeakWorkingSetBytes = peakWorkingSet,
            BaselinePrivateBytes = baselinePrivateBytes,
            PeakPrivateBytes = peakPrivateBytes,
            PeakTransientWorkspaceBytes = peakWorkspaceBytes,
            PeakSpillBytes = peakSpillBytes,
            PeakStoredBytes = peakStoredBytes,
            OutputBytes = outputBytes,
            Observations = observations,
            IncompleteDiskReads = incompleteDiskReads,
            MaxSampleGapMs = maxSampleGapMs
        };
    }

    private static long DirectoryBytes(string path, ref int incompleteReads)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Native spill files can disappear or enter delete-pending state between enumeration and stat.
            incompleteReads++;
            return 0;
        }
    }
}
