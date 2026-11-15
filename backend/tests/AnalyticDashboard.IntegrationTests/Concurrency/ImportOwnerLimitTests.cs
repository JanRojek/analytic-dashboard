using System.Data.Common;

using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;
using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class ImportOwnerLimitTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ImportOwnerLimitTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentClaims_ShouldSerializeOneOwner_AndAllowDifferentOwners(
        bool sameOwner)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, sameOwner ? owner : Guid.NewGuid()], cancellationToken);
        try
        {
            var gate = new SqlGate("WITH candidate_project AS MATERIALIZED");
            await using var firstContext = CreateContext(gate);
            await using var secondContext = CreateContext();
            var firstTask = CreateRepository(firstContext).ClaimNextPendingAsync(cancellationToken);
            ImportJobWorkItem? second;
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);

                second = await CreateRepository(secondContext).ClaimNextPendingAsync(cancellationToken);

                if (sameOwner)
                {
                    Assert.Null(second);
                    var pending = await ReadJobAsync(seed.Jobs[1].Id, cancellationToken);
                    Assert.Equal(ImportJobStatus.Pending, pending.Status);
                    Assert.Equal(0L, pending.AttemptNumber);
                    Assert.Null(pending.LeaseExpiresAtUtc);
                    Assert.Null(pending.StartedAtUtc);
                }
                else
                {
                    Assert.NotNull(second);
                    Assert.Equal(seed.Jobs[1].Id, second.Job.Id);
                    Assert.Equal(1L, second.AttemptNumber);
                    Assert.NotNull(second.Job.LeaseExpiresAtUtc);
                }
                Assert.Null(secondContext.Database.CurrentTransaction);
            }
            finally
            {
                gate.Release.TrySetResult();
                var first = Assert.IsType<ImportJobWorkItem>(await firstTask.WaitAsync(cancellationToken));
                Assert.Equal(seed.Jobs[0].Id, first.Job.Id);
                Assert.Equal(1L, first.AttemptNumber);
            }

            await using var observer = CreateContext();
            Assert.Equal(sameOwner ? 1 : 2, await observer.ImportJobs.CountAsync(
                job => job.Status == ImportJobStatus.Running,
                cancellationToken
            ));
            await AssertOwnerLimitAsync(observer, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Claim_ShouldRecheckRunning_AfterSelectionPredatesTheOtherClaimCommit()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner], cancellationToken);
        try
        {
            var gate = new SqlGate(ownerSelection: true);
            await using var staleContext = CreateContext(gate);
            await using var winnerContext = CreateContext();
            var staleTask = CreateRepository(staleContext).ClaimNextPendingAsync(cancellationToken);
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);

                var winner = Assert.IsType<ImportJobWorkItem>(
                    await CreateRepository(winnerContext).ClaimNextPendingAsync(cancellationToken)
                );
                Assert.Equal(seed.Jobs[0].Id, winner.Job.Id);
            }
            finally
            {
                gate.Release.TrySetResult();
            }

            Assert.Null(await staleTask.WaitAsync(cancellationToken));
            Assert.Equal(1, gate.ClaimStatements);
            Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed.Jobs[1].Id, cancellationToken)).Status);
            await AssertOwnerLimitAsync(winnerContext, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalOutcome_ShouldReleaseTheOwnerSlotAcrossProjects(
        bool complete)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner], cancellationToken);
        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var first = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));

            if (complete)
            {
                var result = await CreateResultAsync(seed, 0, cancellationToken);
                Assert.Equal(
                    ImportJobCompletionOutcome.Completed,
                    await repository.CompleteAsync(
                        first,
                        result.StorageKey,
                        result.RowCount,
                        result.ColumnCount,
                        cancellationToken
                    )
                );
                Assert.True(await Storage.ExistsAsync(result.StorageKey, cancellationToken));
            }
            else
            {
                Assert.True(await repository.TryFailAsync(first, "Invalid CSV", cancellationToken));
            }

            Assert.False(await repository.TryAbandonAsync(first, cancellationToken));
            var second = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );

            Assert.Equal(seed.Jobs[1].Id, second.Job.Id);
            Assert.Equal(1L, second.AttemptNumber);
            Assert.NotNull(second.Job.LeaseExpiresAtUtc);
            var previous = await ReadJobAsync(first.Job.Id, cancellationToken);
            Assert.Equal(complete ? ImportJobStatus.Completed : ImportJobStatus.Failed, previous.Status);
            Assert.Null(previous.LeaseExpiresAtUtc);
            await AssertOwnerLimitAsync(context, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Claim_ShouldSkipAnOwnersCommittedRunningJob_AndItsOlderPendingJobs()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner, Guid.NewGuid()], cancellationToken);
        try
        {
            await using var firstContext = CreateContext();
            await using var nextContext = CreateContext();
            Assert.NotNull(await CreateRepository(firstContext).ClaimNextPendingAsync(cancellationToken));

            var next = Assert.IsType<ImportJobWorkItem>(
                await CreateRepository(nextContext).ClaimNextPendingAsync(cancellationToken)
            );

            Assert.Equal(seed.Jobs[2].Id, next.Job.Id);
            var waiting = await ReadJobAsync(seed.Jobs[1].Id, cancellationToken);
            Assert.Equal(ImportJobStatus.Pending, waiting.Status);
            Assert.Equal(0L, waiting.AttemptNumber);
            Assert.Null(waiting.LeaseExpiresAtUtc);
            await AssertOwnerLimitAsync(nextContext, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Recovery_ShouldPreserveTheOwnerLimit_AndFenceThePreviousAttempt()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner], cancellationToken);
        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var old = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var oldResult = await CreateResultAsync(seed, 0, cancellationToken);
            await ExpireAsync(seed.Jobs[0].Id, cancellationToken);

            Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));
            Assert.True(await repository.RecoverNextExpiredAsync(cancellationToken));
            var current = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );

            Assert.Equal(old.Job.Id, current.Job.Id);
            Assert.Equal(2L, current.AttemptNumber);
            Assert.False(await repository.TryRenewLeaseAsync(old, cancellationToken));
            Assert.False(await repository.TryAbandonAsync(old, cancellationToken));
            Assert.False(await repository.TryFailAsync(old, "Stale failure", cancellationToken));
            Assert.Equal(
                ImportJobCompletionOutcome.OwnershipLost,
                await repository.CompleteAsync(
                    old,
                    oldResult.StorageKey,
                    oldResult.RowCount,
                    oldResult.ColumnCount,
                    cancellationToken
                )
            );
            Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));
            Assert.True(await repository.TryFailAsync(current, "Current attempt failed", cancellationToken));
            var waiting = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            Assert.Equal(seed.Jobs[1].Id, waiting.Job.Id);
            Assert.True(await Storage.ExistsAsync(oldResult.StorageKey, cancellationToken));
            await AssertOwnerLimitAsync(context, cancellationToken);
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
    public async Task ClaimAndActiveSourceDeletion_ShouldRespectBothLockOrders(
        bool deleteProject,
        bool claimFirst)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner], cancellationToken);
        try
        {
            var gate = new SqlGate(claimFirst
                ? "WITH candidate_project AS MATERIALIZED"
                : deleteProject ? "FOR UPDATE OF p" : "FOR NO KEY UPDATE OF project");
            await using var claiming = CreateContext(claimFirst ? gate : null);
            await using var deleting = CreateContext(claimFirst ? null : gate);
            await using var observer = CreateContext();
            await claiming.Database.OpenConnectionAsync(cancellationToken);
            await deleting.Database.OpenConnectionAsync(cancellationToken);
            var claimPid = ((NpgsqlConnection)claiming.Database.GetDbConnection()).ProcessID;
            var deletePid = ((NpgsqlConnection)deleting.Database.GetDbConnection()).ProcessID;
            Task? deletion = null;
            Task<ImportJobWorkItem?>? claim = null;
            try
            {
                if (claimFirst)
                {
                    claim = CreateRepository(claiming).ClaimNextPendingAsync(cancellationToken);
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    deletion = DeleteAsync(deleting, seed, deleteProject, active: true, cancellationToken);
                    await PostgresLockObserver.WaitForBlockerAsync(observer, deletePid, claimPid, cancellationToken);
                }
                else
                {
                    deletion = DeleteAsync(deleting, seed, deleteProject, active: true, cancellationToken);
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    claim = CreateRepository(claiming).ClaimNextPendingAsync(cancellationToken);
                    var claimed = Assert.IsType<ImportJobWorkItem>(await claim.WaitAsync(cancellationToken));
                    Assert.Equal(seed.Jobs[1].Id, claimed.Job.Id);
                }
            }
            finally
            {
                gate.Release.TrySetResult();
                if (claim is not null)
                {
                    Assert.NotNull(await claim.WaitAsync(cancellationToken));
                }
                if (deletion is not null)
                {
                    await deletion.WaitAsync(cancellationToken);
                }
            }

            Assert.Null(await CreateRepository(observer).ClaimNextPendingAsync(cancellationToken));
            Assert.Equal(2, await observer.Datasets.CountAsync(cancellationToken));
            Assert.False(await observer.StorageCleanupJobs.AnyAsync(cancellationToken));
            await AssertOwnerLimitAsync(observer, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletingFinishedSource_ShouldAllowAClaimInTheSameOwnersOtherProject(
        bool deleteProject)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner], cancellationToken);
        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var first = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, 0, cancellationToken);
            Assert.Equal(
                ImportJobCompletionOutcome.Completed,
                await repository.CompleteAsync(
                    first,
                    result.StorageKey,
                    result.RowCount,
                    result.ColumnCount,
                    cancellationToken
                )
            );
            var gate = new SqlGate(deleteProject ? "FOR UPDATE OF p" : "FOR NO KEY UPDATE OF project");
            await using var deleting = CreateContext(gate);
            var deletion = DeleteAsync(deleting, seed, deleteProject, active: false, cancellationToken);
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);

                var next = Assert.IsType<ImportJobWorkItem>(
                    await repository.ClaimNextPendingAsync(cancellationToken)
                );

                Assert.Equal(seed.Jobs[1].Id, next.Job.Id);
            }
            finally
            {
                gate.Release.TrySetResult();
                await deletion.WaitAsync(cancellationToken);
            }

            Assert.False(await context.Datasets.AnyAsync(dataset => dataset.Id == seed.Datasets[0].Id, cancellationToken));
            Assert.True(await context.StorageCleanupJobs.AnyAsync(
                job => job.DatasetId == seed.Datasets[0].Id,
                cancellationToken
            ));
            Assert.True(await Storage.ExistsAsync(result.StorageKey, cancellationToken));
            await AssertOwnerLimitAsync(context, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeartbeatAndRecovery_ShouldRespectBothLockOrders_WithoutInvalidPublication(
        bool heartbeatFirst)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync([Guid.NewGuid()], cancellationToken);
        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var attempt = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, 0, cancellationToken);
            var gate = new SqlGate(heartbeatFirst
                ? "SET \"LeaseExpiresAtUtc\""
                : "FROM import_jobs AS expired_job");
            await using var heartbeatContext = CreateContext(heartbeatFirst ? gate : null);
            await using var recoveryContext = CreateContext(heartbeatFirst ? null : gate);
            await using var observer = CreateContext();
            await heartbeatContext.Database.OpenConnectionAsync(cancellationToken);
            await recoveryContext.Database.OpenConnectionAsync(cancellationToken);

            if (heartbeatFirst)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() + interval '5 seconds'
                    WHERE "Id" = {attempt.Job.Id}
                    """, cancellationToken);
                var originalExpiry = (await ReadJobAsync(attempt.Job.Id, cancellationToken)).LeaseExpiresAtUtc!.Value;
                var renewing = CreateRepository(heartbeatContext).TryRenewLeaseAsync(attempt, cancellationToken);
                try
                {
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    Assert.Equal(1, gate.AffectedRows);
                    await WaitForDatabaseExpiryAsync(observer, originalExpiry, cancellationToken);

                    Assert.False(await CreateRepository(recoveryContext).RecoverNextExpiredAsync(cancellationToken));
                }
                finally
                {
                    gate.Release.TrySetResult();
                    Assert.True(await renewing.WaitAsync(cancellationToken));
                }

                Assert.False(await CreateRepository(recoveryContext).RecoverNextExpiredAsync(cancellationToken));
                Assert.Equal(
                    ImportJobCompletionOutcome.Completed,
                    await repository.CompleteAsync(
                        attempt,
                        result.StorageKey,
                        result.RowCount,
                        result.ColumnCount,
                        cancellationToken
                    )
                );
                Assert.Equal(result.StorageKey, (await context.DatasetVersions.AsNoTracking()
                    .SingleAsync(version => version.Id == seed.Versions[0].Id, cancellationToken)).StorageKey);
            }
            else
            {
                await ExpireAsync(attempt.Job.Id, cancellationToken);
                var recovering = CreateRepository(recoveryContext).RecoverNextExpiredAsync(cancellationToken);
                Task<bool>? renewing = null;
                try
                {
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    renewing = CreateRepository(heartbeatContext).TryRenewLeaseAsync(attempt, cancellationToken);
                    await PostgresLockObserver.WaitForBlockerAsync(
                        observer,
                        ((NpgsqlConnection)heartbeatContext.Database.GetDbConnection()).ProcessID,
                        ((NpgsqlConnection)recoveryContext.Database.GetDbConnection()).ProcessID,
                        cancellationToken
                    );
                }
                finally
                {
                    gate.Release.TrySetResult();
                    Assert.True(await recovering.WaitAsync(cancellationToken));
                    if (renewing is not null)
                    {
                        Assert.False(await renewing.WaitAsync(cancellationToken));
                    }
                }

                Assert.Equal(
                    ImportJobCompletionOutcome.OwnershipLost,
                    await repository.CompleteAsync(
                        attempt,
                        result.StorageKey,
                        result.RowCount,
                        result.ColumnCount,
                        cancellationToken
                    )
                );
                Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(attempt.Job.Id, cancellationToken)).Status);
                Assert.Null((await context.Datasets.AsNoTracking()
                    .SingleAsync(dataset => dataset.Id == seed.Datasets[0].Id, cancellationToken)).CurrentVersionId);
                var next = Assert.IsType<ImportJobWorkItem>(
                    await repository.ClaimNextPendingAsync(cancellationToken)
                );
                Assert.Equal(2L, next.AttemptNumber);
            }

            Assert.True(await Storage.ExistsAsync(result.StorageKey, cancellationToken));
            await AssertOwnerLimitAsync(context, cancellationToken);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private IFileStorage Storage => _fixture.Services.GetRequiredService<IFileStorage>();

    private AppDbContext CreateContext(IInterceptor? interceptor = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }
        return new AppDbContext(options.Options);
    }

    private static ImportJobRepository CreateRepository(AppDbContext context)
    {
        return new ImportJobRepository(
            context,
            new ProjectWriteTransactionFactory(context),
            Options.Create(new ImportJobOptions()),
            NullLogger<ImportJobRepository>.Instance
        );
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private async Task<Seed> SeedAsync(Guid[] owners, CancellationToken cancellationToken)
    {
        var projects = new List<Project>();
        var datasets = new List<Dataset>();
        var versions = new List<DatasetVersion>();
        var jobs = new List<ImportJob>();
        await using var context = CreateContext();
        for (var index = 0; index < owners.Length; index++)
        {
            var project = new Project(
                owners[index],
                $"Owner import project {index}"
            );
            var dataset = new Dataset(
                project.Id,
                "Owner dataset"
            );
            var version = new DatasetVersion(
                dataset.Id,
                1,
                "source.csv"
            );
            var job = new ImportJob(
                version.Id,
                "source.csv",
                $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
            );
            context.AddRange(project, dataset, version, job);
            context.Entry(job).Property(item => item.CreatedAtUtc).CurrentValue =
                new DateTime(2026, 1, 1, 0, index, 0, DateTimeKind.Utc);
            projects.Add(project);
            datasets.Add(dataset);
            versions.Add(version);
            jobs.Add(job);
            await using var source = new MemoryStream("Country,Amount\nPL,10\n"u8.ToArray());
            await Storage.SaveAsync(job.SourceStorageKey, source, cancellationToken);
        }
        await context.SaveChangesAsync(cancellationToken);
        return new Seed(projects, datasets, versions, jobs);
    }

    private async Task<ImportJob> ReadJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        return await context.ImportJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken);
    }

    private async Task ExpireAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() - interval '1 second'
            WHERE "Id" = {jobId}
            """, cancellationToken);
    }

    private async Task<DuckDbCsvImportResult> CreateResultAsync(
        Seed seed,
        int index,
        CancellationToken cancellationToken)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>().ImportAsync(
            seed.Jobs[index].SourceStorageKey,
            $"datasets/{seed.Datasets[index].Id:N}/versions/{seed.Versions[index].Id:N}/attempts",
            cancellationToken
        );
    }

    private static async Task AssertOwnerLimitAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var owners = await (
            from job in context.ImportJobs
            join version in context.DatasetVersions on job.DatasetVersionId equals version.Id
            join dataset in context.Datasets on version.DatasetId equals dataset.Id
            join project in context.Projects on dataset.ProjectId equals project.Id
            where job.Status == ImportJobStatus.Running
            select project.OwnerId
        ).ToListAsync(cancellationToken);
        Assert.All(owners.GroupBy(owner => owner), group => Assert.Single(group));
    }

    private static async Task DeleteAsync(
        AppDbContext context,
        Seed seed,
        bool deleteProject,
        bool active,
        CancellationToken cancellationToken)
    {
        var cleanup = new StorageCleanupQueue(context);
        if (deleteProject)
        {
            Assert.Equal(
                active ? ProjectDeletionOutcome.ImportInProgress : ProjectDeletionOutcome.Deleted,
                await new ProjectDeletionStore(context, cleanup).DeleteAsync(
                    seed.Projects[0].Id,
                    seed.Projects[0].OwnerId,
                    cancellationToken
                )
            );
        }
        else
        {
            Assert.Equal(
                active ? DatasetDeletionOutcome.ImportInProgress : DatasetDeletionOutcome.Deleted,
                await new DatasetDeletionStore(
                    context,
                    cleanup,
                    new ProjectWriteTransactionFactory(context)
                ).DeleteAsync(
                    seed.Datasets[0].Id,
                    seed.Projects[0].Id,
                    seed.Projects[0].OwnerId,
                    cancellationToken
                )
            );
        }
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var context = CreateContext();
        var projects = seed.Projects.Select(project => project.Id).ToArray();
        var datasets = seed.Datasets.Select(dataset => dataset.Id).ToArray();
        await context.Projects.Where(project => projects.Contains(project.Id)).ExecuteDeleteAsync(CancellationToken.None);
        await context.StorageCleanupJobs.Where(job => datasets.Contains(job.DatasetId)).ExecuteDeleteAsync(CancellationToken.None);
        foreach (var dataset in seed.Datasets)
        {
            await Storage.DeleteDirectoryAsync($"datasets/{dataset.Id:N}", CancellationToken.None);
        }
    }

    private static async Task WaitForDatabaseExpiryAsync(
        AppDbContext observer,
        DateTime originalExpiry,
        CancellationToken cancellationToken)
    {
        await observer.Database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT clock_timestamp() >= @expiry",
            (NpgsqlConnection)observer.Database.GetDbConnection()
        );
        command.Parameters.AddWithValue("expiry", originalExpiry);
        while (!Equals(true, await command.ExecuteScalarAsync(cancellationToken)))
        {
            // Heartbeat is held after its UPDATE; this only throttles observation of the original server-side lease.
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private sealed record Seed(
        IReadOnlyList<Project> Projects,
        IReadOnlyList<Dataset> Datasets,
        IReadOnlyList<DatasetVersion> Versions,
        IReadOnlyList<ImportJob> Jobs
    );

    private sealed class SqlGate : DbCommandInterceptor
    {
        private readonly string? _marker;
        private readonly bool _ownerSelection;
        public int ClaimStatements { get; private set; }
        public int? AffectedRows { get; private set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SqlGate(string? marker = null, bool ownerSelection = false)
        {
            _marker = marker;
            _ownerSelection = ownerSelection;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("WITH candidate_project AS MATERIALIZED", StringComparison.Ordinal))
            {
                ClaimStatements++;
            }
            if ((_marker is not null && command.CommandText.Contains(_marker, StringComparison.Ordinal))
                || (_ownerSelection && result.FieldCount == 1 && result.GetName(0) == "OwnerId"))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (_marker is not null && command.CommandText.Contains(_marker, StringComparison.Ordinal))
            {
                AffectedRows = result;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
