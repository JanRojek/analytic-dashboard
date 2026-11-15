using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using DuckDB.NET.Data;
using DuckDB.NET.Native;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AnalyticDashboard.IntegrationTests.Performance;

public sealed partial class CsvImportPerformanceTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "Performance")]
    public async Task Tuning_ShouldCompareBoundedConversionAndParquetReads()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromMinutes(25));
        var token = deadline.Token;
        var mode = Environment.GetEnvironmentVariable("APERTURE_DUCKDB_TUNING_MODE") ?? "screen";
        var reportPath = Environment.GetEnvironmentVariable("APERTURE_DUCKDB_TUNING_REPORT");
        var root = Directory.CreateTempSubdirectory("aperture-duckdb-tuning-");
        Assert.True(new DriveInfo(root.Root.FullName).AvailableFreeSpace > 20L * 1024 * 1024 * 1024);
        var samples = new List<object>();
        var privateGuardBytes = 5L * 1024 * 1024 * 1024;
        var cases = mode switch
        {
            "screen" => new[] { (MiB: 150, Entropy: false) },
            "scale" => new[] { (MiB: 500, Entropy: false), (MiB: 1024, Entropy: false) },
            "balance" => new[] { (MiB: 500, Entropy: false), (MiB: 1024, Entropy: false) },
            "tradeoff" => new[] { (MiB: 500, Entropy: false), (MiB: 1024, Entropy: false) },
            "codec" => new[] { (MiB: 1024, Entropy: false), (MiB: 1024, Entropy: true) },
            "entropy" => new[] { (MiB: 1024, Entropy: true) },
            _ => throw new ArgumentException("Use screen, scale, balance, tradeoff, codec or entropy.")
        };
        var profiles = CreateTuningProfiles(mode).ToArray();
        var repetitions = mode == "screen" ? 3 : 2;
        var header = new
        {
            Mode = mode,
            Runtime = Environment.Version.ToString(),
            CpuCount = Environment.ProcessorCount,
            AvailableGcMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            ProcessPrivateGuardBytes = privateGuardBytes,
            TempDirectoryGuardBytes = 16L * 1024 * 1024 * 1024,
            SamplingIntervalMs = 50,
            FrameworkDefaults = new
            {
                MaxRequestBodyBytes = new KestrelServerOptions().Limits.MaxRequestBodySize,
                MultipartBodyBytes = new FormOptions().MultipartBodyLengthLimit,
                FormMemoryBufferBytes = new FormOptions().MemoryBufferThreshold
            }
        };

        async Task SaveReportAsync()
        {
            var json = JsonSerializer.Serialize(
                new { Header = header, Samples = samples },
                new JsonSerializerOptions { WriteIndented = true }
            );
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                await File.WriteAllTextAsync(reportPath, json, token);
            }
        }

        try
        {
            foreach (var sourceCase in cases)
            {
                var sourcePath = Path.Combine(root.FullName, "source.csv");
                var source = await WriteTuningSourceAsync(
                    sourcePath,
                    sourceCase.MiB * 1048576L,
                    sourceCase.Entropy,
                    token
                );
                string? referenceFingerprint = null;
                foreach (var profile in profiles)
                {
                    for (var iteration = 0; iteration < repetitions; iteration++)
                    {
                        var trial = Path.Combine(root.FullName, $"trial-{samples.Count}");
                        Directory.CreateDirectory(trial);
                        var measured = await MeasureTuningBatchAsync(
                            trial,
                            source,
                            profile,
                            privateGuardBytes,
                            token
                        );
                        object? reads = null;
                        var output = measured.Results.FirstOrDefault(result => result.Outcome == "completed");
                        if (output is not null && iteration == 0)
                        {
                            var readResult = await MeasureTuningReadsAsync(
                                output.OutputPath!,
                                source.Rows,
                                token
                            );
                            if (profile.PreserveOrder)
                            {
                                Assert.Equal(0L, readResult.DisplacedRows);
                            }
                            if (profile.PreserveOrder)
                            {
                                Assert.Equal(0L, readResult.DisplacedRows);
                            }
                            referenceFingerprint ??= readResult.Fingerprint;
                            Assert.Equal(referenceFingerprint, readResult.Fingerprint);
                            reads = readResult;
                        }
                        samples.Add(new
                        {
                            SourceMiB = sourceCase.MiB,
                            SourceEntropy = sourceCase.Entropy ? "independent_hash_blocks" : "repeated_hash",
                            SourceBytes = source.Bytes,
                            SourceRows = source.Rows,
                            Profile = profile,
                            Iteration = iteration,
                            Batch = measured,
                            Reads = reads
                        });
                        await SaveReportAsync();
                        DeleteTuningTrial(root.FullName, trial);
                    }
                }
                File.Delete(sourcePath);
            }
            _output.WriteLine(JsonSerializer.Serialize(header));
            _output.WriteLine($"Finished {samples.Count} import batches.");
        }
        finally
        {
            var tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            Assert.StartsWith(tempPrefix, root.FullName, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("aperture-duckdb-tuning-", root.Name, StringComparison.Ordinal);
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static IEnumerable<TuningProfile> CreateTuningProfiles(string mode)
    {
        if (mode == "screen")
        {
            foreach (var memory in new[] { 256, 512, 1024 })
            {
                foreach (var direct in new[] { false, true })
                {
                    foreach (var concurrency in new[] { 1, 2 })
                    {
                        yield return new TuningProfile(
                            direct ? "direct_default" : "table_default",
                            direct,
                            memory,
                            122880,
                            "zstd",
                            true,
                            1,
                            concurrency
                        );
                    }
                }
            }
            foreach (var direct in new[] { false, true })
            {
                foreach (var concurrency in new[] { 1, 2 })
                {
                    yield return new TuningProfile(
                        direct ? "direct_16k" : "table_16k",
                        direct,
                        256,
                        16384,
                        "zstd",
                        true,
                        1,
                        concurrency
                    );
                }
            }
            yield return new TuningProfile(
                "table_32k",
                false,
                256,
                32768,
                "zstd",
                true,
                1,
                1
            );
            yield return new TuningProfile(
                "direct_32k",
                true,
                256,
                32768,
                "zstd",
                true,
                1,
                1
            );
            yield return new TuningProfile(
                "direct_64k",
                true,
                256,
                65536,
                "zstd",
                true,
                1,
                1
            );
            yield return new TuningProfile(
                "zstd_level1",
                true,
                256,
                16384,
                "zstd",
                true,
                1,
                1,
                1
            );
            yield return new TuningProfile(
                "snappy",
                true,
                256,
                16384,
                "snappy",
                true,
                1,
                1
            );
            yield return new TuningProfile(
                "uncompressed",
                true,
                256,
                16384,
                "uncompressed",
                true,
                1,
                1
            );
            yield return new TuningProfile(
                "table_unordered",
                false,
                256,
                122880,
                "zstd",
                false,
                1,
                1
            );
            yield return new TuningProfile(
                "direct_unordered",
                true,
                256,
                122880,
                "zstd",
                false,
                1,
                1
            );
            yield return new TuningProfile(
                "unordered_4threads",
                true,
                256,
                122880,
                "zstd",
                false,
                4,
                1
            );
            yield return new TuningProfile(
                "ordered_16k_4threads",
                true,
                256,
                16384,
                "zstd",
                true,
                4,
                1
            );
            yield return new TuningProfile(
                "unordered_16k_4threads",
                true,
                256,
                16384,
                "zstd",
                false,
                4,
                1
            );
            yield return new TuningProfile(
                "unordered_16mib",
                true,
                256,
                122880,
                "zstd",
                false,
                1,
                1,
                RowGroupMiB: 16
            );
            yield break;
        }

        if (mode == "scale")
        {
            yield return new TuningProfile(
                "table_default",
                false,
                256,
                122880,
                "zstd",
                true,
                1,
                1
            );
            foreach (var concurrency in new[] { 1, 2 })
            {
                yield return new TuningProfile(
                    "table_1g",
                    false,
                    1024,
                    122880,
                    "zstd",
                    true,
                    1,
                    concurrency
                );
                yield return new TuningProfile(
                    "table_16k",
                    false,
                    256,
                    16384,
                    "zstd",
                    true,
                    1,
                    concurrency
                );
                yield return new TuningProfile(
                    "direct_default",
                    true,
                    256,
                    122880,
                    "zstd",
                    true,
                    1,
                    concurrency
                );
                foreach (var memory in new[] { 256, 512, 1024 })
                {
                    yield return new TuningProfile(
                        "direct_16k",
                        true,
                        memory,
                        16384,
                        "zstd",
                        true,
                        1,
                        concurrency
                    );
                }
            }
            yield break;
        }

        if (mode == "balance")
        {
            foreach (var concurrency in new[] { 1, 2 })
            {
                foreach (var level in new[] { 3, 1 })
                {
                    yield return new TuningProfile(
                        "direct_32k",
                        true,
                        256,
                        32768,
                        "zstd",
                        true,
                        1,
                        concurrency,
                        level
                    );
                }
            }
            yield break;
        }

        if (mode == "codec")
        {
            foreach (var concurrency in new[] { 1, 2 })
            {
                yield return new TuningProfile(
                    "direct_default_level1",
                    true,
                    512,
                    122880,
                    "zstd",
                    true,
                    1,
                    concurrency,
                    1
                );
            }
            yield break;
        }

        if (mode == "tradeoff")
        {
            foreach (var concurrency in new[] { 1, 2 })
            {
                yield return new TuningProfile(
                    "direct_default_512",
                    true,
                    512,
                    122880,
                    "zstd",
                    true,
                    1,
                    concurrency
                );
                yield return new TuningProfile(
                    "direct_64k",
                    true,
                    256,
                    65536,
                    "zstd",
                    true,
                    1,
                    concurrency
                );
                yield return new TuningProfile(
                    "direct_64k",
                    true,
                    512,
                    65536,
                    "zstd",
                    true,
                    1,
                    concurrency
                );
            }
            yield break;
        }

        foreach (var concurrency in new[] { 1, 2 })
        {
            yield return new TuningProfile(
                "entropy_direct_default",
                true,
                256,
                122880,
                "zstd",
                true,
                1,
                concurrency
            );
            yield return new TuningProfile(
                "entropy_direct_default_512",
                true,
                512,
                122880,
                "zstd",
                true,
                1,
                concurrency
            );
            yield return new TuningProfile(
                "entropy_direct_16k",
                true,
                256,
                16384,
                "zstd",
                true,
                1,
                concurrency
            );
            yield return new TuningProfile(
                "entropy_direct_32k",
                true,
                256,
                32768,
                "zstd",
                true,
                1,
                concurrency
            );
            yield return new TuningProfile(
                "entropy_direct_64k",
                true,
                256,
                65536,
                "zstd",
                true,
                1,
                concurrency
            );
            yield return new TuningProfile(
                "entropy_zstd_level1",
                true,
                256,
                32768,
                "zstd",
                true,
                1,
                concurrency,
                1
            );
        }
    }

    private static async Task<TuningSource> WriteTuningSourceAsync(
        string path,
        long targetBytes,
        bool entropy,
        CancellationToken cancellationToken)
    {
        const string header = "Id,Category,Payload,Value";
        await using var file = File.Create(path);
        await using var writer = new StreamWriter(file, new UTF8Encoding(false), 65536);
        writer.NewLine = "\n";
        await writer.WriteLineAsync(header.AsMemory(), cancellationToken);
        long bytes = header.Length + 1;
        var rows = 0;
        while (bytes < targetBytes)
        {
            var hash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(rows)));
            var payload = entropy
                ? string.Concat(Enumerable.Range(0, 16).Select(block =>
                    Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes($"{rows}-{block}")))))
                : string.Concat(Enumerable.Repeat(hash, 16));
            var line = $"{rows},Group-{rows % 128},{payload},{rows % 10000}.25";
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            bytes += line.Length + 1;
            rows++;
        }
        await writer.FlushAsync(cancellationToken);
        Assert.Equal(bytes, new FileInfo(path).Length);
        return new TuningSource(path, bytes, rows);
    }

    private static async Task<TuningBatch> MeasureTuningBatchAsync(
        string trial,
        TuningSource source,
        TuningProfile profile,
        long privateGuardBytes,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baselinePrivate = process.PrivateMemorySize64;
        var baselineWorkingSet = process.WorkingSet64;
        var baselineCpu = process.TotalProcessorTime;
        long peakPrivate = baselinePrivate;
        long peakWorkingSet = baselineWorkingSet;
        long peakDisk = 0;
        long peakSpill = 0;
        var incompleteReads = 0;
        var observations = 0;
        var guard = 0;
        var lastSample = Stopwatch.GetTimestamp();
        double maxSampleGapMs = 0;
        using var samplerCancellation = new CancellationTokenSource();

        void Sample()
        {
            process.Refresh();
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakDisk = Math.Max(peakDisk, DirectoryBytes(trial, ref incompleteReads));
            long spill = 0;
            foreach (var directory in Directory.GetDirectories(trial))
            {
                spill += DirectoryBytes(Path.Combine(directory, "spill"), ref incompleteReads);
            }
            peakSpill = Math.Max(peakSpill, spill);
            var now = Stopwatch.GetTimestamp();
            maxSampleGapMs = Math.Max(maxSampleGapMs, Stopwatch.GetElapsedTime(lastSample, now).TotalMilliseconds);
            lastSample = now;
            observations++;
            if (peakPrivate > privateGuardBytes || peakDisk > 16L * 1024 * 1024 * 1024)
            {
                Interlocked.Exchange(ref guard, 1);
                cancellation.Cancel();
            }
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
                while (await timer.WaitForNextTickAsync(samplerCancellation.Token));
            }
            catch (OperationCanceledException) when (samplerCancellation.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        var elapsed = Stopwatch.StartNew();
        TuningConversion[] results;
        try
        {
            var tasks = Enumerable.Range(0, profile.Concurrency).Select(index => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref ready) == profile.Concurrency)
                {
                    startGate.TrySetResult();
                }
                await startGate.Task.WaitAsync(cancellation.Token);
                return await ConvertTuningAsync(
                    trial, index, source, profile, cancellation.Token
                );
            }, cancellation.Token)).ToArray();
            results = await Task.WhenAll(tasks);
            elapsed.Stop();
        }
        finally
        {
            await samplerCancellation.CancelAsync();
            await sampler;
        }
        Sample();
        cancellationToken.ThrowIfCancellationRequested();
        return new TuningBatch(
            elapsed.Elapsed.TotalMilliseconds,
            (process.TotalProcessorTime - baselineCpu).TotalMilliseconds,
            baselinePrivate, peakPrivate, baselineWorkingSet, peakWorkingSet,
            peakDisk, peakSpill, incompleteReads, observations, maxSampleGapMs,
            guard != 0, results
        );
    }

    private static async Task<TuningConversion> ConvertTuningAsync(
        string trial,
        int index,
        TuningSource source,
        TuningProfile profile,
        CancellationToken cancellationToken)
    {
        var work = Path.Combine(trial, index.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(work);
        var input = Path.Combine(work, "source.csv");
        var output = Path.Combine(work, "data.parquet");
        var savedOutput = Path.Combine(work, "saved.parquet");
        var phase = "copy_source";
        var phaseStarted = Stopwatch.GetTimestamp();
        var phases = new Dictionary<string, double>();
        string? schema = null;
        long rowCount = 0;
        try
        {
            File.Copy(source.Path, input, overwrite: false);
            phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            cancellationToken.ThrowIfCancellationRequested();
            var settings = new DuckDBConnectionStringBuilder
            {
                DataSource = DuckDBConnectionStringBuilder.InMemoryDataSource,
                ["threads"] = profile.Threads,
                ["memory_limit"] = $"{profile.MemoryMiB}MiB",
                ["max_temp_directory_size"] = "1024MiB",
                ["temp_directory"] = Path.Combine(work, "spill"),
                ["preserve_insertion_order"] = profile.PreserveOrder
            };
            await using var connection = new DuckDBConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            using var registration = cancellationToken.Register(
                static state => ((DuckDBCommand)state!).Cancel(),
                command
            );
            if (!profile.Direct)
            {
                phase = "materialize_table";
                phaseStarted = Stopwatch.GetTimestamp();
                command.CommandText = "CREATE TABLE imported_dataset AS SELECT * FROM read_csv_auto($input, header=true)";
                command.Parameters.Add(new DuckDBParameter("input", input));
                await command.ExecuteNonQueryAsync(cancellationToken);
                phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                command.Parameters.Clear();
                command.CommandText = "SELECT COUNT(*) FROM imported_dataset";
                rowCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            phase = "write_parquet";
            phaseStarted = Stopwatch.GetTimestamp();
            var query = profile.Direct ? "SELECT * FROM read_csv_auto($input, header=true)" : "SELECT * FROM imported_dataset";
            var level = profile.Codec == "zstd" ? $", COMPRESSION_LEVEL {profile.ZstdLevel}" : "";
            var groupBytes = profile.RowGroupMiB is { } mib ? $", ROW_GROUP_SIZE_BYTES '{mib}MiB'" : "";
            command.CommandText = $"""
                COPY ({query}) TO $output
                (FORMAT parquet, COMPRESSION {profile.Codec}, ROW_GROUP_SIZE {profile.RowGroupRows}{level}{groupBytes})
                """;
            command.Parameters.Clear();
            command.Parameters.Add(new DuckDBParameter("output", output));
            if (profile.Direct)
            {
                command.Parameters.Add(new DuckDBParameter("input", input));
            }
            await command.ExecuteNonQueryAsync(cancellationToken);
            phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;

            phase = "metadata";
            phaseStarted = Stopwatch.GetTimestamp();
            command.Parameters.Clear();
            command.Parameters.Add(new DuckDBParameter("path", output));
            command.CommandText = "SELECT num_rows FROM parquet_file_metadata($path)";
            var copiedRows = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            Assert.Equal(source.Rows, copiedRows);
            if (!profile.Direct)
            {
                Assert.Equal(rowCount, copiedRows);
            }
            rowCount = copiedRows;
            command.CommandText = "DESCRIBE SELECT * FROM read_parquet($path)";
            var columns = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add(reader.GetString(0) + ":" + reader.GetString(1));
                }
            }
            Assert.Equal(4, columns.Count);
            schema = string.Join("|", columns);
            Assert.Equal("Id:BIGINT|Category:VARCHAR|Payload:VARCHAR|Value:DOUBLE", schema);
            phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;

            phase = "copy_result";
            phaseStarted = Stopwatch.GetTimestamp();
            File.Copy(output, savedOutput, overwrite: false);
            phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            return new TuningConversion(
                "completed", phase, null, rowCount, schema, new FileInfo(output).Length, phases, savedOutput
            );
        }
        catch (DuckDBException exception) when (exception.ErrorType == DuckDBErrorType.OutOfMemory)
        {
            phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            return new TuningConversion("oom", phase, exception.ErrorType.ToString(), 0, schema, 0, phases, null);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            phases[phase] = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            return new TuningConversion("cancelled", phase, null, 0, schema, 0, phases, null);
        }
    }

    private static async Task<TuningReads> MeasureTuningReadsAsync(
        string path,
        int rows,
        CancellationToken cancellationToken)
    {
        var readCopy = path + ".read";
        var copying = Stopwatch.StartNew();
        File.Copy(path, readCopy, overwrite: false);
        copying.Stop();
        var measurements = new List<object>();
        string? fingerprint = null;
        long displacedRows = 0;
        long rowGroups = 0;
        long maxGroupRows = 0;
        string? codec = null;
        foreach (var threads in new[] { 1, 4 })
        {
            var settings = new DuckDBConnectionStringBuilder
            {
                DataSource = DuckDBConnectionStringBuilder.InMemoryDataSource,
                ["threads"] = threads,
                ["memory_limit"] = "512MiB"
            };
            await using var connection = new DuckDBConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Parameters.Add(new DuckDBParameter("path", readCopy));
            if (threads == 1)
            {
                command.CommandText = """
                    SELECT COUNT(DISTINCT row_group_id), MAX(row_group_num_rows), MIN(compression)
                    FROM parquet_metadata($path)
                    """;
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    Assert.True(await reader.ReadAsync(cancellationToken));
                    rowGroups = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                    maxGroupRows = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                    codec = reader.GetString(2);
                }
                command.CommandText = """
                    SELECT COUNT(*), CAST(SUM(Id) AS BIGINT), CAST(SUM(length(Payload)) AS BIGINT),
                           bit_xor(hash(Id, Category, Payload, Value))
                    FROM read_parquet($path)
                    """;
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    Assert.True(await reader.ReadAsync(cancellationToken));
                    Assert.Equal(rows, Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
                    Assert.Equal((long)rows * (rows - 1) / 2, Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture));
                    Assert.Equal((long)rows * 1024, Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture));
                    fingerprint = Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture);
                }
                command.CommandText = """
                    SELECT COUNT(*) FROM (
                        SELECT Id, row_number() OVER () - 1 AS position FROM read_parquet($path)
                    ) WHERE Id <> position
                    """;
                displacedRows = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            var queries = new Dictionary<string, string>
            {
                ["aggregation"] = "SELECT Category, SUM(Value) FROM read_parquet($path) GROUP BY Category ORDER BY Category",
                ["numeric_profile"] = "SELECT COUNT(*) - COUNT(Value), MIN(Value), MAX(Value), AVG(Value) FROM read_parquet($path)",
                ["page_middle"] = $"SELECT * FROM read_parquet($path) LIMIT 100 OFFSET {rows / 2}",
                ["selective_1percent"] = $"SELECT SUM(Value) FROM read_parquet($path) WHERE Id BETWEEN {rows / 2} AND {rows / 2 + rows / 100}"
            };
            foreach (var query in queries)
            {
                var timings = new List<double>();
                for (var repeat = 0; repeat < 4; repeat++)
                {
                    command.CommandText = query.Value;
                    var elapsed = Stopwatch.StartNew();
                    await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            for (var column = 0; column < reader.FieldCount; column++)
                            {
                                _ = reader.GetValue(column);
                            }
                        }
                    }
                    if (repeat > 0)
                    {
                        timings.Add(elapsed.Elapsed.TotalMilliseconds);
                    }
                }
                timings.Sort();
                measurements.Add(new { Threads = threads, Query = query.Key, TimingsMs = timings, MedianMs = timings[1] });
            }
        }
        return new TuningReads(
            fingerprint!, displacedRows, rowGroups, maxGroupRows, codec!, copying.Elapsed.TotalMilliseconds, measurements
        );
    }

    private static void DeleteTuningTrial(string root, string trial)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(trial);
        Assert.StartsWith(prefix, target, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(target, recursive: true);
    }

    private sealed record TuningProfile(
        string Name,
        bool Direct,
        int MemoryMiB,
        int RowGroupRows,
        string Codec,
        bool PreserveOrder,
        int Threads,
        int Concurrency,
        int ZstdLevel = 3,
        int? RowGroupMiB = null
    );

    private sealed record TuningSource(string Path, long Bytes, int Rows);

    private sealed record TuningConversion(
        string Outcome,
        string LastPhase,
        string? ErrorType,
        long Rows,
        string? Schema,
        long OutputBytes,
        IReadOnlyDictionary<string, double> PhasesMs,
        [property: JsonIgnore] string? OutputPath
    );

    private sealed record TuningBatch(
        double ElapsedMs,
        double CpuMs,
        long BaselinePrivateBytes,
        long PeakPrivateBytes,
        long BaselineWorkingSetBytes,
        long PeakWorkingSetBytes,
        long PeakTrialBytes,
        long PeakSpillBytes,
        int IncompleteDiskReads,
        int Observations,
        double MaxSampleGapMs,
        bool GuardTriggered,
        IReadOnlyList<TuningConversion> Results
    );

    private sealed record TuningReads(
        string Fingerprint,
        long DisplacedRows,
        long RowGroups,
        long MaxGroupRows,
        string Codec,
        double FileCopyMs,
        IReadOnlyList<object> Queries
    );
}
