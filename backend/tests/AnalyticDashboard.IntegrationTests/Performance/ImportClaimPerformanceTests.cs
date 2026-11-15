using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;

using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;
using NpgsqlTypes;

namespace AnalyticDashboard.IntegrationTests.Performance;

public sealed class ImportClaimPerformanceTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ImportClaimPerformanceTests(ApiFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Performance")]
    public async Task Claim_ShouldBeMeasuredAgainstRepresentativeQueueDistributions()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var cancellationToken = deadline.Token;
        var capture = new CommandCapture();
        using var scope = _fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(capture)
            .Options;
        await using var setup = new AppDbContext(options);
        await setup.Database.ExecuteSqlRawAsync("""
            INSERT INTO projects ("Id", "OwnerId", "Name", "CreatedAtUtc")
            SELECT md5('plan-project-' || n)::uuid, md5('plan-owner-' || n)::uuid,
                   'Claim plan project ' || n, timestamptz '2026-01-01'
            FROM generate_series(1, 500) AS n;
            INSERT INTO datasets ("Id", "ProjectId", "Name", "CreatedAtUtc")
            SELECT md5('plan-dataset-' || n)::uuid, md5('plan-project-' || n)::uuid,
                   'Claim plan dataset', timestamptz '2026-01-01'
            FROM generate_series(1, 500) AS n;
            INSERT INTO dataset_versions
                ("Id", "DatasetId", "VersionNumber", "OriginalFileName", "Status", "CreatedAtUtc")
            SELECT md5('plan-version-' || owner || '-' || number)::uuid,
                   md5('plan-dataset-' || owner)::uuid, number, 'source.csv',
                   CASE WHEN number <= 50 THEN 'Failed' ELSE 'Pending' END,
                   timestamptz '2026-01-01' + (owner * 1000 + number) * interval '1 second'
            FROM generate_series(1, 500) AS owner CROSS JOIN generate_series(1, 90) AS number;
            INSERT INTO import_jobs
                ("Id", "DatasetVersionId", "SourceFileName", "SourceStorageKey", "Status",
                 "AttemptNumber", "CreatedAtUtc", "CompletedAtUtc", "ErrorMessage")
            SELECT md5('plan-job-' || owner || '-' || number)::uuid,
                   md5('plan-version-' || owner || '-' || number)::uuid,
                   CASE WHEN number <= 50 THEN 'history.csv' ELSE 'queued.csv' END,
                   'benchmark/source.csv',
                   CASE WHEN number <= 50 THEN 'Failed' ELSE 'Pending' END,
                   CASE WHEN number <= 50 THEN 3 ELSE 0 END,
                   timestamptz '2026-01-01' + (owner * 1000 + number) * interval '1 second',
                   CASE WHEN number <= 50 THEN timestamptz '2026-01-10' ELSE NULL END,
                   CASE WHEN number <= 50 THEN 'Historical failure' ELSE NULL END
            FROM generate_series(1, 500) AS owner CROSS JOIN generate_series(1, 90) AS number;
            """, cancellationToken);

        var reports = new List<object>();
        foreach (var scenario in new[] { ("eligible", 0), ("busy_90_percent", 450), ("all_owners_busy", 500), ("empty", -1) })
        {
            await setup.Database.ExecuteSqlRawAsync("""
                UPDATE import_jobs SET "Status" = 'Pending', "AttemptNumber" = 0,
                    "StartedAtUtc" = NULL, "CompletedAtUtc" = NULL, "ErrorMessage" = NULL, "LeaseExpiresAtUtc" = NULL
                WHERE "SourceFileName" = 'queued.csv';
                UPDATE dataset_versions SET "Status" = 'Pending' WHERE "VersionNumber" > 50;
                """, cancellationToken);
            if (scenario.Item2 >= 0)
            {
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE import_jobs SET "Status" = 'Running', "AttemptNumber" = 1,
                        "StartedAtUtc" = clock_timestamp(), "LeaseExpiresAtUtc" = clock_timestamp() + interval '10 minutes'
                    WHERE "Id" IN (
                        SELECT md5('plan-job-' || n || '-51')::uuid
                        FROM generate_series(1, {scenario.Item2}) AS n
                    );
                    """, cancellationToken);
            }
            else
            {
                await setup.Database.ExecuteSqlRawAsync("""
                    UPDATE import_jobs SET "Status" = 'Failed', "AttemptNumber" = 1,
                        "CompletedAtUtc" = clock_timestamp(), "ErrorMessage" = 'Finished queue'
                    WHERE "SourceFileName" = 'queued.csv';
                    UPDATE dataset_versions SET "Status" = 'Failed' WHERE "VersionNumber" > 50;
                    """, cancellationToken);
            }
            await setup.Database.ExecuteSqlRawAsync(
                "ANALYZE projects; ANALYZE datasets; ANALYZE dataset_versions; ANALYZE import_jobs;",
                cancellationToken
            );

            capture.Clear();
            var durations = new List<double>();
            for (var iteration = 0; iteration < 11; iteration++)
            {
                await using var context = new AppDbContext(options);
                var repository = new ImportJobRepository(
                    context,
                    new ProjectWriteTransactionFactory(context),
                    Options.Create(new ImportJobOptions()),
                    NullLogger<ImportJobRepository>.Instance
                );
                var stopwatch = Stopwatch.StartNew();
                var claimed = await repository.ClaimNextPendingAsync(cancellationToken);
                stopwatch.Stop();
                if (iteration >= 2)
                {
                    durations.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
                Assert.Equal(scenario.Item2 is >= 0 and < 500, claimed is not null);
                if (claimed is not null)
                {
                    await context.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE import_jobs SET "Status" = 'Pending', "AttemptNumber" = 0,
                            "StartedAtUtc" = NULL, "LeaseExpiresAtUtc" = NULL
                        WHERE "Id" = {claimed.Job.Id};
                        """, cancellationToken);
                }
            }

            var selection = Assert.IsType<CapturedCommand>(capture.Selection);
            var ownerPlan = await ExplainAsync(connectionString, selection, null, cancellationToken);
            JsonElement? claimPlan = null;
            if (capture.Claim is { } claim)
            {
                claimPlan = await ExplainAsync(connectionString, claim, capture.OwnerLock, cancellationToken);
            }
            durations.Sort();
            var summary = new
            {
                Scenario = scenario.Item1,
                Owners = 500,
                HistoricalFailed = 25000,
                Terminal = scenario.Item2 < 0 ? 45000 : 25000,
                Pending = scenario.Item2 < 0 ? 0 : 20000 - scenario.Item2,
                Running = Math.Max(0, scenario.Item2),
                Samples = durations,
                MedianMs = durations[durations.Count / 2],
                P95Ms = durations[^1],
                OwnerSelectionPlan = ownerPlan,
                ClaimPlan = claimPlan
            };
            reports.Add(summary);
            _output.WriteLine(JsonSerializer.Serialize(summary));
        }

        await using var versionConnection = new NpgsqlConnection(connectionString);
        await versionConnection.OpenAsync(cancellationToken);
        await using var versionCommand = new NpgsqlCommand("SELECT version()", versionConnection);
        var version = await versionCommand.ExecuteScalarAsync(cancellationToken);
        var report = JsonSerializer.Serialize(
            new { PostgreSql = version, Runtime = Environment.Version.ToString(), Scenarios = reports },
            new JsonSerializerOptions { WriteIndented = true }
        );
        var reportPath = Environment.GetEnvironmentVariable("APERTURE_IMPORT_CLAIM_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            await File.WriteAllTextAsync(reportPath, report, cancellationToken);
        }
    }

    private static async Task<JsonElement> ExplainAsync(
        string connectionString,
        CapturedCommand query,
        CapturedCommand? ownerLock,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (ownerLock is not null)
        {
            await using var locking = ownerLock.Create(connection, transaction);
            Assert.Equal(true, await locking.ExecuteScalarAsync(cancellationToken));
        }
        await using var explaining = query.Create(connection, transaction, explain: true);
        var json = Assert.IsType<string>(await explaining.ExecuteScalarAsync(cancellationToken));
        using var document = JsonDocument.Parse(json);
        var plan = document.RootElement.Clone();
        await transaction.RollbackAsync(cancellationToken);
        return plan;
    }

    private sealed record CapturedCommand(
        string Sql,
        IReadOnlyList<(string Name, object Value, NpgsqlDbType Type)> Parameters)
    {
        public NpgsqlCommand Create(NpgsqlConnection connection, NpgsqlTransaction transaction, bool explain = false)
        {
            var command = new NpgsqlCommand(
                (explain ? "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " : "") + Sql,
                connection,
                transaction
            );
            foreach (var parameter in Parameters)
            {
                command.Parameters.Add(new NpgsqlParameter(parameter.Name, parameter.Type)
                {
                    Value = parameter.Value
                });
            }
            return command;
        }
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public CapturedCommand? Selection { get; private set; }
        public CapturedCommand? OwnerLock { get; private set; }
        public CapturedCommand? Claim { get; private set; }

        public void Clear()
        {
            Selection = null;
            OwnerLock = null;
            Claim = null;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var capture = new CapturedCommand(
                command.CommandText,
                command.Parameters.Cast<NpgsqlParameter>().Select(parameter => (
                    parameter.ParameterName,
                    parameter.Value is IEnumerable<Guid> ids ? ids.ToArray()
                        : parameter.Value is Array array ? array.Clone() : parameter.Value ?? DBNull.Value,
                    parameter.NpgsqlDbType
                )).ToArray()
            );
            if (result.FieldCount == 1 && result.GetName(0) == "OwnerId")
            {
                Selection = capture;
            }
            else if (command.CommandText.Contains("pg_try_advisory_xact_lock", StringComparison.Ordinal))
            {
                OwnerLock = capture;
            }
            else if (command.CommandText.Contains("WITH candidate_project AS MATERIALIZED", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM import_jobs AS pending_job", StringComparison.Ordinal))
            {
                Claim = capture;
            }
            return ValueTask.FromResult(result);
        }
    }
}
