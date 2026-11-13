using System.Data.Common;

using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class AtomicImportClaimTests : IClassFixture<ApiFixture>
{
    private const string ClaimSqlMarker = "WITH candidate_project AS MATERIALIZED";
    private readonly ApiFixture _fixture;

    public AtomicImportClaimTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentClaims_ShouldHaveExactlyOneWinner_WithoutChangingLosersTrackedJob()
    {
        using var timeout = CreateDeadline();
        var cancellationToken = timeout.Token;

        var seed = await SeedAsync(1, sameProject: true, cancellationToken);
        try
        {
            var gate = new SqlGate(ClaimSqlMarker);
            await using var winnerContext = CreateContext(gate);
            await using var loserContext = CreateContext();
            var tracked = await loserContext.ImportJobs.SingleAsync(cancellationToken);
            var before = await ReadJobAsync(
                loserContext,
                seed.Jobs[0].Id,
                cancellationToken
            );

            var winnerTask = new ImportJobRepository(winnerContext)
                .ClaimNextPendingAsync(cancellationToken);
            ImportJobWorkItem? winner;
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);

                var loser = await new ImportJobRepository(loserContext)
                    .ClaimNextPendingAsync(cancellationToken);

                Assert.Null(loser);
                AssertJobSnapshotsEqual(
                    before,
                    await ReadJobAsync(loserContext, tracked.Id, cancellationToken)
                );
                Assert.Equal(ImportJobStatus.Pending, tracked.Status);
                Assert.Equal(0L, tracked.AttemptNumber);
                Assert.Null(tracked.StartedAtUtc);
                Assert.False(loserContext.ChangeTracker.HasChanges());
                Assert.Null(loserContext.Database.CurrentTransaction);
            }
            finally
            {
                gate.Release.TrySetResult();
                winner = await winnerTask.WaitAsync(cancellationToken);
            }

            Assert.NotNull(winner);
            Assert.Equal(tracked.Id, winner.Job.Id);
            Assert.Equal(1L, winner.AttemptNumber);
            Assert.Equal(1L, winner.Job.AttemptNumber);
            Assert.Null(winnerContext.Database.CurrentTransaction);
            AssertJobSnapshotsEqual(
                before with
                {
                    Status = ImportJobStatus.Running,
                    AttemptNumber = 1,
                    StartedAtUtc = winner.Job.StartedAtUtc
                },
                await ReadJobAsync(loserContext, tracked.Id, cancellationToken)
            );
            await AssertUnchangedDependenciesAsync(loserContext, seed, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Claim_ShouldSkipBusyProject_AndClaimIndependentPendingBeforeFirstCommit()
    {
        using var timeout = CreateDeadline();
        var cancellationToken = timeout.Token;

        var seed = await SeedAsync(2, sameProject: false, cancellationToken);
        try
        {
            var gate = new SqlGate(ClaimSqlMarker);
            await using var firstContext = CreateContext(gate);
            await using var secondContext = CreateContext();
            var firstTask = new ImportJobRepository(firstContext)
                .ClaimNextPendingAsync(cancellationToken);

            ImportJobWorkItem? first;
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);

                var second = await new ImportJobRepository(secondContext)
                    .ClaimNextPendingAsync(cancellationToken);

                Assert.NotNull(second);
                Assert.Equal(seed.Jobs[1].Id, second.Job.Id);
                Assert.Equal(1L, second.AttemptNumber);
                Assert.Null(secondContext.Database.CurrentTransaction);
            }
            finally
            {
                gate.Release.TrySetResult();
                first = await firstTask.WaitAsync(cancellationToken);
            }

            Assert.NotNull(first);
            Assert.Equal(seed.Jobs[0].Id, first.Job.Id);
            Assert.Equal(1L, first.AttemptNumber);
            await AssertUnchangedDependenciesAsync(secondContext, seed, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Claim_ShouldSkipLockedJob_AndAllowMultipleRunningJobsInSameProject()
    {
        using var timeout = CreateDeadline();
        var cancellationToken = timeout.Token;

        var seed = await SeedAsync(2, sameProject: true, cancellationToken);
        try
        {
            await using var lockingContext = CreateContext();
            await using var claimingContext = CreateContext();
            var repository = new ImportJobRepository(claimingContext);
            await using (var transaction = await lockingContext.Database.BeginTransactionAsync(
                cancellationToken
            ))
            {
                await lockingContext.ImportJobs.FromSqlInterpolated($"""
                    SELECT * FROM import_jobs WHERE "Id" = {seed.Jobs[0].Id} FOR UPDATE
                    """).AsNoTracking().ToListAsync(cancellationToken);

                var second = await repository.ClaimNextPendingAsync(cancellationToken);

                Assert.NotNull(second);
                Assert.Equal(seed.Jobs[1].Id, second.Job.Id);
                Assert.Equal(1L, second.AttemptNumber);
                Assert.Null(claimingContext.Database.CurrentTransaction);
                Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(
                    lockingContext,
                    seed.Jobs[0].Id,
                    cancellationToken
                )).Status);

                await transaction.RollbackAsync(cancellationToken);
            }

            var first = await repository.ClaimNextPendingAsync(cancellationToken);

            Assert.NotNull(first);
            Assert.Equal(seed.Jobs[0].Id, first.Job.Id);
            Assert.Equal(1L, first.AttemptNumber);
            Assert.Null(claimingContext.Database.CurrentTransaction);
            Assert.Equal(2, await claimingContext.ImportJobs.CountAsync(
                item => item.Status == ImportJobStatus.Running,
                cancellationToken
            ));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Claim_ShouldIncrementPersistedAttempt_RefreshTracking_AndNeverReclaimRunning()
    {
        using var timeout = CreateDeadline();
        var cancellationToken = timeout.Token;

        var seed = await SeedAsync(
            1,
            sameProject: true,
            cancellationToken,
            attemptNumber: 7
        );
        try
        {
            await using var context = CreateContext();
            var tracked = await context.ImportJobs.SingleAsync(cancellationToken);
            Assert.Equal(7L, tracked.AttemptNumber);
            var repository = new ImportJobRepository(context);

            var claimed = await repository.ClaimNextPendingAsync(cancellationToken);

            Assert.NotNull(claimed);
            Assert.Same(tracked, claimed.Job);
            Assert.Equal(8L, claimed.AttemptNumber);
            Assert.Equal(8L, tracked.AttemptNumber);
            Assert.Equal(ImportJobStatus.Running, tracked.Status);
            Assert.NotNull(tracked.StartedAtUtc);
            Assert.False(context.ChangeTracker.HasChanges());
            Assert.Null(context.Database.CurrentTransaction);

            var beforeLosingClaim = await ReadJobAsync(
                context,
                tracked.Id,
                cancellationToken
            );

            Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));

            AssertJobSnapshotsEqual(
                beforeLosingClaim,
                await ReadJobAsync(context, tracked.Id, cancellationToken)
            );
            tracked.MarkFailed("Test failure after claim");
            await repository.SaveChangesAsync(cancellationToken);
            Assert.Equal(
                8L,
                (await ReadJobAsync(context, tracked.Id, cancellationToken)).AttemptNumber
            );
            context.Entry(tracked).Property(item => item.AttemptNumber).CurrentValue = 9;
            Assert.Equal(8L, claimed.AttemptNumber);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Claim_ShouldRollbackStatusTimestampAndAttempt_WhenCommandResultCannotBeConsumed()
    {
        using var timeout = CreateDeadline();
        var cancellationToken = timeout.Token;

        var seed = await SeedAsync(1, sameProject: true, cancellationToken);
        try
        {
            var gate = new SqlGate(ClaimSqlMarker, failAfterRelease: true);
            await using var context = CreateContext(gate);
            await using var observer = CreateContext();
            var before = await ReadJobAsync(
                observer,
                seed.Jobs[0].Id,
                cancellationToken
            );
            var claiming = new ImportJobRepository(context)
                .ClaimNextPendingAsync(cancellationToken);
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                gate.Release.TrySetResult();
            }

            await Assert.ThrowsAsync<IOException>(() => claiming);

            AssertJobSnapshotsEqual(
                before,
                await ReadJobAsync(observer, seed.Jobs[0].Id, cancellationToken)
            );
            Assert.Null(context.Database.CurrentTransaction);
            var successful = await new ImportJobRepository(observer)
                .ClaimNextPendingAsync(cancellationToken);
            Assert.NotNull(successful);
            Assert.Equal(1L, successful.AttemptNumber);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ClaimAndDeletion_ShouldPreserveActiveImport_InBothLockOrders(
        bool deleteProject,
        bool claimFirst)
    {
        using var timeout = CreateDeadline();
        var cancellationToken = timeout.Token;

        var seed = await SeedAsync(1, sameProject: true, cancellationToken);
        try
        {
            var marker = claimFirst
                ? ClaimSqlMarker
                : deleteProject
                    ? "FOR UPDATE OF p"
                    : "FOR NO KEY UPDATE OF project";

            var gate = new SqlGate(marker);
            await using var claimContext = CreateContext(claimFirst ? gate : null);
            await using var deleteContext = CreateContext(claimFirst ? null : gate);
            await using var observer = CreateContext();
            await claimContext.Database.OpenConnectionAsync(cancellationToken);
            await deleteContext.Database.OpenConnectionAsync(cancellationToken);
            var claimPid = ((NpgsqlConnection)claimContext.Database.GetDbConnection())
                .ProcessID;
            var deletePid = ((NpgsqlConnection)deleteContext.Database.GetDbConnection())
                .ProcessID;
            var repository = new ImportJobRepository(claimContext);

            if (claimFirst)
            {
                var claiming = repository.ClaimNextPendingAsync(cancellationToken);
                Task? deleting = null;
                try
                {
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    deleting = DeleteResourceExpectImportInProgressAsync(
                        deleteContext,
                        seed,
                        deleteProject,
                        cancellationToken
                    );
                    await WaitForBlockerAsync(
                        observer,
                        deletePid,
                        claimPid,
                        cancellationToken
                    );
                }
                finally
                {
                    gate.Release.TrySetResult();
                    Assert.NotNull(await claiming.WaitAsync(cancellationToken));
                    if (deleting is not null)
                    {
                        await deleting.WaitAsync(cancellationToken);
                    }
                }
            }
            else
            {
                var deleting = DeleteResourceExpectImportInProgressAsync(
                    deleteContext,
                    seed,
                    deleteProject,
                    cancellationToken
                );
                try
                {
                    await gate.Reached.Task.WaitAsync(cancellationToken);

                    Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));

                    var unchanged = await ReadJobAsync(
                        observer,
                        seed.Jobs[0].Id,
                        cancellationToken
                    );
                    Assert.Equal(ImportJobStatus.Pending, unchanged.Status);
                    Assert.Equal(0L, unchanged.AttemptNumber);
                    Assert.Null(unchanged.StartedAtUtc);
                }
                finally
                {
                    gate.Release.TrySetResult();
                    await deleting.WaitAsync(cancellationToken);
                }
                Assert.NotNull(await repository.ClaimNextPendingAsync(cancellationToken));
            }

            var saved = await ReadJobAsync(
                observer,
                seed.Jobs[0].Id,
                cancellationToken
            );
            Assert.Equal(ImportJobStatus.Running, saved.Status);
            Assert.Equal(1L, saved.AttemptNumber);
            await AssertUnchangedDependenciesAsync(observer, seed, cancellationToken);
            Assert.False(await observer.StorageCleanupJobs.AnyAsync(cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private static async Task DeleteResourceExpectImportInProgressAsync(
        AppDbContext context,
        Seed seed,
        bool deleteProject,
        CancellationToken cancellationToken)
    {
        var cleanup = new StorageCleanupQueue(context);

        if (deleteProject)
        {
            var outcome = await new ProjectDeletionStore(context, cleanup).DeleteAsync(
                seed.Projects[0].Id,
                seed.Projects[0].OwnerId,
                cancellationToken
            );

            Assert.Equal(ProjectDeletionOutcome.ImportInProgress, outcome);
        }
        else
        {
            var outcome = await new DatasetDeletionStore(
                context,
                cleanup,
                new ProjectWriteTransactionFactory(context)
            ).DeleteAsync(
                seed.Datasets[0].Id,
                seed.Projects[0].Id,
                seed.Projects[0].OwnerId,
                cancellationToken
            );

            Assert.Equal(DatasetDeletionOutcome.ImportInProgress, outcome);
        }
    }

    private AppDbContext CreateContext(DbCommandInterceptor? interceptor = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .Database.GetConnectionString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }
        return new AppDbContext(options.Options);
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private async Task<Seed> SeedAsync(
        int count,
        bool sameProject,
        CancellationToken cancellationToken,
        long attemptNumber = 0)
    {
        await using var context = CreateContext();
        var projects = new List<Project>();
        var datasets = new List<Dataset>();
        var versions = new List<DatasetVersion>();
        var jobs = new List<ImportJob>();

        for (var index = 0; index < count; index++)
        {
            var project = sameProject && index > 0
                ? projects[0]
                : new Project(Guid.NewGuid(), "Claim project");
            if (!projects.Contains(project))
            {
                projects.Add(project);
            }

            var dataset = new Dataset(project.Id, "Claim dataset");
            var version = new DatasetVersion(dataset.Id, 1, "source.csv");
            var job = new ImportJob(
                version.Id,
                "source.csv",
                $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
            );
            context.AddRange(project, dataset, version, job);
            context.Entry(job).Property(item => item.CreatedAtUtc).CurrentValue =
                new DateTime(2026, 1, 1, 0, index, 0, DateTimeKind.Utc);
            context.Entry(job).Property(item => item.AttemptNumber).CurrentValue = attemptNumber;

            datasets.Add(dataset);
            versions.Add(version);
            jobs.Add(job);
        }

        await context.SaveChangesAsync(cancellationToken);

        return new Seed(projects, datasets, versions, jobs);
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var context = CreateContext();
        var ids = seed.Projects.Select(item => item.Id).ToArray();

        await context.Projects
            .Where(item => ids.Contains(item.Id))
            .ExecuteDeleteAsync(CancellationToken.None);
    }

    private static async Task<JobSnapshot> ReadJobAsync(
        AppDbContext context,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs
            .AsNoTracking()
            .SingleAsync(item => item.Id == jobId, cancellationToken);
        return new JobSnapshot(
            job.Id,
            job.DatasetVersionId,
            job.Status,
            job.AttemptNumber,
            job.SourceFileName,
            job.SourceStorageKey,
            job.ErrorMessage,
            job.CreatedAtUtc,
            job.StartedAtUtc,
            job.CompletedAtUtc
        );
    }

    private static void AssertJobSnapshotsEqual(JobSnapshot expected, JobSnapshot actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.DatasetVersionId, actual.DatasetVersionId);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.AttemptNumber, actual.AttemptNumber);
        Assert.Equal(expected.SourceFileName, actual.SourceFileName);
        Assert.Equal(expected.SourceStorageKey, actual.SourceStorageKey);
        Assert.Equal(expected.ErrorMessage, actual.ErrorMessage);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.StartedAtUtc, actual.StartedAtUtc);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
    }

    private static async Task AssertUnchangedDependenciesAsync(
        AppDbContext context,
        Seed seed,
        CancellationToken cancellationToken)
    {
        foreach (var dataset in seed.Datasets)
        {
            var saved = await context.Datasets
                .AsNoTracking()
                .SingleAsync(item => item.Id == dataset.Id, cancellationToken);
            Assert.Equal(dataset.Name, saved.Name);
            Assert.Equal(dataset.ProjectId, saved.ProjectId);
            Assert.Null(saved.CurrentVersionId);
        }

        foreach (var version in seed.Versions)
        {
            var saved = await context.DatasetVersions
                .AsNoTracking()
                .SingleAsync(item => item.Id == version.Id, cancellationToken);
            Assert.Equal(DatasetVersionStatus.Pending, saved.Status);
            Assert.Equal(version.OriginalFileName, saved.OriginalFileName);
            Assert.Null(saved.StorageKey);
            Assert.Null(saved.RowCount);
            Assert.Null(saved.ColumnCount);
        }
    }

    private static async Task WaitForBlockerAsync(
        AppDbContext observer,
        int waitingPid,
        int blockerPid,
        CancellationToken cancellationToken)
    {
        await observer.Database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT @blocker = ANY(pg_blocking_pids(@waiting))",
            (NpgsqlConnection)observer.Database.GetDbConnection()
        );
        command.Parameters.AddWithValue("blocker", blockerPid);
        command.Parameters.AddWithValue("waiting", waitingPid);

        while (!Equals(true, await command.ExecuteScalarAsync(cancellationToken)))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed record Seed(
        IReadOnlyList<Project> Projects,
        IReadOnlyList<Dataset> Datasets,
        IReadOnlyList<DatasetVersion> Versions,
        IReadOnlyList<ImportJob> Jobs
    );

    private sealed record JobSnapshot(
        Guid Id,
        Guid DatasetVersionId,
        ImportJobStatus Status,
        long AttemptNumber,
        string SourceFileName,
        string SourceStorageKey,
        string? ErrorMessage,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc
    );

    private sealed class SqlGate : DbCommandInterceptor
    {
        private readonly string _marker;
        private readonly bool _failAfterRelease;

        public SqlGate(string marker, bool failAfterRelease = false)
        {
            _marker = marker;
            _failAfterRelease = failAfterRelease;
        }

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(_marker, StringComparison.Ordinal))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);

                if (_failAfterRelease)
                {
                    throw new IOException("Simulated failure after the claim statement, before commit.");
                }
            }

            return result;
        }
    }
}
