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

public sealed class ImportFinalizationTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ImportFinalizationTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Complete_ShouldPublishAtomically_AndRecognizeOnlyTheExactRepeatedResult()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var workItem = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, cancellationToken);

            var outcome = await repository.CompleteAsync(
                workItem,
                result.StorageKey,
                result.RowCount,
                result.ColumnCount,
                cancellationToken
            );

            Assert.Equal(ImportJobCompletionOutcome.Completed, outcome);

            var completed = await ReadStateAsync(seed, cancellationToken);
            Assert.Equal(ImportJobStatus.Completed, completed.JobStatus);
            Assert.Equal(1L, completed.AttemptNumber);
            Assert.NotNull(completed.CompletedAtUtc);
            Assert.Equal(DatasetVersionStatus.Ready, completed.VersionStatus);
            Assert.Equal(result.StorageKey, completed.StorageKey);
            Assert.Equal(result.RowCount, completed.RowCount);
            Assert.Equal(result.ColumnCount, completed.ColumnCount);
            Assert.Equal(seed.Version.Id, completed.CurrentVersionId);
            Assert.True(await ResultExistsAsync(result, cancellationToken));

            Assert.Equal(
                ImportJobCompletionOutcome.AlreadyCompleted,
                await repository.CompleteAsync(
                    workItem,
                    result.StorageKey,
                    result.RowCount,
                    result.ColumnCount,
                    cancellationToken
                )
            );
            Assert.Equal(
                ImportJobCompletionOutcome.OwnershipLost,
                await repository.CompleteAsync(
                    workItem,
                    result.StorageKey,
                    result.RowCount + 1,
                    result.ColumnCount,
                    cancellationToken
                )
            );
            Assert.False(await repository.TryFailAsync(workItem, "Late failure", cancellationToken));

            AssertStateEqual(completed, await ReadStateAsync(seed, cancellationToken));
            Assert.Null(context.Database.CurrentTransaction);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleAttempt_ShouldNotPublishOrFail_AfterARealNewClaim(
        bool newerAttemptCompleted)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            await using var oldContext = CreateContext();
            await using var newContext = CreateContext();
            var oldRepository = CreateRepository(oldContext);
            var oldAttempt = Assert.IsType<ImportJobWorkItem>(
                await oldRepository.ClaimNextPendingAsync(cancellationToken)
            );
            var oldResult = await CreateResultAsync(seed, cancellationToken);
            var newAttempt = Assert.IsType<ImportJobWorkItem>(
                await RequeueAndClaimAsync(newContext, seed, oldAttempt.AttemptNumber, cancellationToken)
            );
            Assert.Equal(2L, newAttempt.AttemptNumber);
            var newResult = await CreateResultAsync(seed, cancellationToken);

            if (newerAttemptCompleted)
            {
                Assert.Equal(
                    ImportJobCompletionOutcome.Completed,
                    await CreateRepository(newContext).CompleteAsync(
                        newAttempt,
                        newResult.StorageKey,
                        newResult.RowCount,
                        newResult.ColumnCount,
                        cancellationToken
                    )
                );
            }

            var before = await ReadStateAsync(seed, cancellationToken);
            oldContext.Entry(oldAttempt.Job).Property(job => job.AttemptNumber).CurrentValue = 2;

            Assert.Equal(
                ImportJobCompletionOutcome.OwnershipLost,
                await oldRepository.CompleteAsync(
                    oldAttempt,
                    oldResult.StorageKey,
                    oldResult.RowCount,
                    oldResult.ColumnCount,
                    cancellationToken
                )
            );
            Assert.False(await oldRepository.TryFailAsync(oldAttempt, "Stale failure", cancellationToken));

            AssertStateEqual(before, await ReadStateAsync(seed, cancellationToken));
            Assert.True(await ResultExistsAsync(oldResult, cancellationToken));
            Assert.True(await ResultExistsAsync(newResult, cancellationToken));
            Assert.False(await oldRepository.IsCompletedAsync(
                oldAttempt,
                newResult.StorageKey,
                newResult.RowCount,
                newResult.ColumnCount,
                cancellationToken
            ));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalization_ShouldRollbackAllChanges_WhenTheLastUpdateFails(
        bool failImport)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            var fault = new JobUpdateFault();
            await using var context = CreateContext(fault);
            var repository = CreateRepository(context);
            var workItem = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, cancellationToken);
            var before = await ReadStateAsync(seed, cancellationToken);

            if (failImport)
            {
                await Assert.ThrowsAsync<IOException>(() =>
                    repository.TryFailAsync(workItem, "Conversion failed", cancellationToken)
                );
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => repository.CompleteAsync(
                    workItem,
                    result.StorageKey,
                    result.RowCount,
                    result.ColumnCount,
                    cancellationToken
                ));
            }

            Assert.Equal(1, fault.ExecutedUpdates);
            AssertStateEqual(before, await ReadStateAsync(seed, cancellationToken));
            Assert.Null(context.Database.CurrentTransaction);
            Assert.True(await ResultExistsAsync(result, cancellationToken));
            Assert.False(await repository.IsCompletedAsync(
                workItem,
                result.StorageKey,
                result.RowCount,
                result.ColumnCount,
                cancellationToken
            ));
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
    public async Task FinalizationAndOwnershipChange_ShouldSerializeInBothOrders(
        bool failImport,
        bool ownershipChangeFirst)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            var gate = new SqlGate(ownershipChangeFirst
                ? "WITH candidate_project AS MATERIALIZED"
                : "FOR UPDATE OF job");
            await using var finalizer = CreateContext(ownershipChangeFirst ? null : gate);
            await using var nextOwner = CreateContext(ownershipChangeFirst ? gate : null);
            await using var observer = CreateContext();
            var repository = CreateRepository(finalizer);
            var oldAttempt = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, cancellationToken);
            await finalizer.Database.OpenConnectionAsync(cancellationToken);
            await nextOwner.Database.OpenConnectionAsync(cancellationToken);
            var finalizerPid = ((NpgsqlConnection)finalizer.Database.GetDbConnection()).ProcessID;
            var nextOwnerPid = ((NpgsqlConnection)nextOwner.Database.GetDbConnection()).ProcessID;

            Task<bool>? finalizing = null;
            Task<ImportJobWorkItem?>? changingOwner = null;
            try
            {
                if (ownershipChangeFirst)
                {
                    changingOwner = RequeueAndClaimAsync(
                        nextOwner,
                        seed,
                        oldAttempt.AttemptNumber,
                        cancellationToken
                    );
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    finalizing = FinalizeAttemptAsync(
                        repository,
                        oldAttempt,
                        result,
                        failImport,
                        cancellationToken
                    );
                    await PostgresLockObserver.WaitForBlockerAsync(observer, finalizerPid, nextOwnerPid, cancellationToken);
                }
                else
                {
                    finalizing = FinalizeAttemptAsync(
                        repository,
                        oldAttempt,
                        result,
                        failImport,
                        cancellationToken
                    );
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    changingOwner = RequeueAndClaimAsync(
                        nextOwner,
                        seed,
                        oldAttempt.AttemptNumber,
                        cancellationToken
                    );
                    await PostgresLockObserver.WaitForBlockerAsync(observer, nextOwnerPid, finalizerPid, cancellationToken);
                }
            }
            finally
            {
                gate.Release.TrySetResult();
                if (finalizing is not null)
                {
                    await finalizing.WaitAsync(cancellationToken);
                }
                if (changingOwner is not null)
                {
                    await changingOwner.WaitAsync(cancellationToken);
                }
            }

            Assert.NotNull(finalizing);
            Assert.NotNull(changingOwner);
            Assert.Equal(!ownershipChangeFirst, await finalizing);
            var nextAttempt = await changingOwner;
            var saved = await ReadStateAsync(seed, cancellationToken);

            if (ownershipChangeFirst)
            {
                Assert.NotNull(nextAttempt);
                Assert.Equal(2L, saved.AttemptNumber);
                Assert.Equal(ImportJobStatus.Running, saved.JobStatus);
                Assert.Equal(DatasetVersionStatus.Pending, saved.VersionStatus);
                Assert.Null(saved.CurrentVersionId);
                Assert.Null(saved.StorageKey);
                Assert.Null(saved.ErrorMessage);
                Assert.Null(saved.CompletedAtUtc);
            }
            else
            {
                Assert.Null(nextAttempt);
                Assert.Equal(1L, saved.AttemptNumber);
                Assert.Equal(failImport ? ImportJobStatus.Failed : ImportJobStatus.Completed, saved.JobStatus);
                Assert.Equal(
                failImport ? DatasetVersionStatus.Failed : DatasetVersionStatus.Ready,
                saved.VersionStatus
            );
                Assert.Equal(failImport ? null : seed.Version.Id, saved.CurrentVersionId);
                Assert.Equal(failImport ? null : result.StorageKey, saved.StorageKey);
                Assert.NotNull(saved.CompletedAtUtc);
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Processor_ShouldVerifyActualCommittedResult_WhenCommitAcknowledgementThrows()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            var fault = new PublicationCommitFault();
            await using var context = CreateContext(fault);
            using var scope = _fixture.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var processor = new ImportJobProcessor(
                CreateRepository(context),
                new DuckDbCsvImporter(
                    storage,
                    NullLogger<DuckDbCsvImporter>.Instance,
                    Options.Create(new ImportJobOptions())
                ),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<IOptions<ImportJobOptions>>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                NullLogger<ImportJobProcessor>.Instance
            );

            Assert.True(await processor.ProcessNextAsync(cancellationToken));

            Assert.Equal(2, fault.CommitCount);
            Assert.True(fault.PublicationCommitted);
            Assert.Null(context.Database.CurrentTransaction);
            var saved = await ReadStateAsync(seed, cancellationToken);
            Assert.Equal(ImportJobStatus.Completed, saved.JobStatus);
            Assert.Equal(DatasetVersionStatus.Ready, saved.VersionStatus);
            Assert.Equal(seed.Version.Id, saved.CurrentVersionId);
            Assert.Equal(1L, saved.AttemptNumber);
            Assert.NotNull(saved.StorageKey);
            Assert.True(await storage.ExistsAsync(saved.StorageKey, cancellationToken));
            Assert.Equal(ImportJobStatus.Running, context.ImportJobs.Local.Single().Status);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Processor_ShouldDiscardStaleSuccessOrFailure_WithoutTouchingNewPublication(
        bool conversionFails)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            await using var oldContext = CreateContext();
            await using var newContext = CreateContext();
            using var scope = _fixture.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var gate = new ConversionGate(
                storage,
                conversionFails
            );
            var processor = new ImportJobProcessor(
                CreateRepository(oldContext),
                new DuckDbCsvImporter(
                    gate,
                    NullLogger<DuckDbCsvImporter>.Instance,
                    Options.Create(new ImportJobOptions())
                ),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<IOptions<ImportJobOptions>>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                NullLogger<ImportJobProcessor>.Instance
            );
            var processing = processor.ProcessNextAsync(cancellationToken);
            State published;

            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);
                Assert.Null(oldContext.Database.CurrentTransaction);
                var newer = Assert.IsType<ImportJobWorkItem>(
                    await RequeueAndClaimAsync(newContext, seed, 1, cancellationToken)
                );
                var result = await CreateResultAsync(seed, cancellationToken);

                Assert.Equal(
                    ImportJobCompletionOutcome.Completed,
                    await CreateRepository(newContext).CompleteAsync(
                        newer,
                        result.StorageKey,
                        result.RowCount,
                        result.ColumnCount,
                        cancellationToken
                    )
                );
                published = await ReadStateAsync(seed, cancellationToken);
            }
            finally
            {
                gate.Release.TrySetResult();
                Assert.True(await processing.WaitAsync(cancellationToken));
            }

            AssertStateEqual(published, await ReadStateAsync(seed, cancellationToken));
            Assert.Equal(0, gate.DeleteCalls);
            Assert.True(await storage.ExistsAsync(published.StorageKey!, cancellationToken));
            if (!conversionFails)
            {
                Assert.NotNull(gate.SavedKey);
                Assert.NotEqual(published.StorageKey, gate.SavedKey);
                Assert.True(await storage.ExistsAsync(gate.SavedKey, cancellationToken));
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_ShouldWaitForPublication_AndQueueCleanupAfterDatabaseDeletion(
        bool deleteProject)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            var gate = new SqlGate("FOR UPDATE OF job");
            await using var finalizer = CreateContext(gate);
            await using var deletingContext = CreateContext();
            await using var observer = CreateContext();
            var repository = CreateRepository(finalizer);
            var attempt = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, cancellationToken);
            await finalizer.Database.OpenConnectionAsync(cancellationToken);
            await deletingContext.Database.OpenConnectionAsync(cancellationToken);
            var finalizerPid = ((NpgsqlConnection)finalizer.Database.GetDbConnection()).ProcessID;
            var deletePid = ((NpgsqlConnection)deletingContext.Database.GetDbConnection()).ProcessID;
            var finalizing = repository.CompleteAsync(
                attempt,
                result.StorageKey,
                result.RowCount,
                result.ColumnCount,
                cancellationToken
            );

            Task? deleting = null;
            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);
                deleting = DeleteResourceAsync(
                    deletingContext,
                    seed,
                    deleteProject,
                    cancellationToken
                );
                await PostgresLockObserver.WaitForBlockerAsync(observer, deletePid, finalizerPid, cancellationToken);
            }
            finally
            {
                gate.Release.TrySetResult();
                Assert.Equal(ImportJobCompletionOutcome.Completed, await finalizing.WaitAsync(cancellationToken));
                if (deleting is not null)
                {
                    await deleting.WaitAsync(cancellationToken);
                }
            }

            Assert.False(await observer.Datasets.AnyAsync(item => item.Id == seed.Dataset.Id, cancellationToken));
            Assert.False(await observer.ImportJobs.AnyAsync(item => item.Id == seed.Job.Id, cancellationToken));
            Assert.True(await observer.StorageCleanupJobs.AnyAsync(
                item => item.DatasetId == seed.Dataset.Id,
                cancellationToken
            ));
            Assert.True(await ResultExistsAsync(result, cancellationToken));
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
            Assert.False(await repository.TryFailAsync(attempt, "After deletion", cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalOwnershipChange_ShouldRespectTheJobRowLock_InBothOrders(
        bool ownershipChangeFirst)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            var gate = new SqlGate("FOR UPDATE OF job");
            await using var finalizer = CreateContext(ownershipChangeFirst ? null : gate);
            await using var nextOwner = CreateContext();
            await using var observer = CreateContext();
            var repository = CreateRepository(finalizer);
            var attempt = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var result = await CreateResultAsync(seed, cancellationToken);
            await finalizer.Database.OpenConnectionAsync(cancellationToken);
            await nextOwner.Database.OpenConnectionAsync(cancellationToken);
            var finalizerPid = ((NpgsqlConnection)finalizer.Database.GetDbConnection()).ProcessID;
            var ownerPid = ((NpgsqlConnection)nextOwner.Database.GetDbConnection()).ProcessID;

            if (ownershipChangeFirst)
            {
                await using var transaction = await nextOwner.Database.BeginTransactionAsync(cancellationToken);
                Assert.Equal(1, await ChangeAttemptTokenAsync(
                    nextOwner,
                    seed.Job.Id,
                    attempt.AttemptNumber,
                    cancellationToken
                ));

                var completing = repository.CompleteAsync(
                    attempt,
                    result.StorageKey,
                    result.RowCount,
                    result.ColumnCount,
                    cancellationToken
                );
                try
                {
                    await PostgresLockObserver.WaitForBlockerAsync(observer, finalizerPid, ownerPid, cancellationToken);
                }
                finally
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                Assert.Equal(
                    ImportJobCompletionOutcome.OwnershipLost,
                    await completing.WaitAsync(cancellationToken)
                );
            }
            else
            {
                var completing = repository.CompleteAsync(
                    attempt,
                    result.StorageKey,
                    result.RowCount,
                    result.ColumnCount,
                    cancellationToken
                );
                Task<int>? changingToken = null;
                try
                {
                    await gate.Reached.Task.WaitAsync(cancellationToken);
                    changingToken = ChangeAttemptTokenAsync(
                        nextOwner,
                        seed.Job.Id,
                        attempt.AttemptNumber,
                        cancellationToken
                    );
                    await PostgresLockObserver.WaitForBlockerAsync(observer, ownerPid, finalizerPid, cancellationToken);
                }
                finally
                {
                    gate.Release.TrySetResult();
                    Assert.Equal(
                        ImportJobCompletionOutcome.Completed,
                        await completing.WaitAsync(cancellationToken)
                    );
                    if (changingToken is not null)
                    {
                        Assert.Equal(0, await changingToken.WaitAsync(cancellationToken));
                    }
                }
            }

            var saved = await ReadStateAsync(seed, cancellationToken);
            Assert.Equal(ownershipChangeFirst ? 2L : 1L, saved.AttemptNumber);
            Assert.Equal(
                ownershipChangeFirst ? ImportJobStatus.Running : ImportJobStatus.Completed,
                saved.JobStatus
            );
            Assert.Equal(
                ownershipChangeFirst ? DatasetVersionStatus.Pending : DatasetVersionStatus.Ready,
                saved.VersionStatus
            );
            Assert.Equal(ownershipChangeFirst ? null : seed.Version.Id, saved.CurrentVersionId);
            Assert.Equal(ownershipChangeFirst ? null : result.StorageKey, saved.StorageKey);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task AlreadyCompleted_ShouldNotRepublishAnOldVersion_AfterANewerVersionIsCurrent()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);

        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var first = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            var firstResult = await CreateResultAsync(seed, cancellationToken);
            Assert.Equal(
                ImportJobCompletionOutcome.Completed,
                await repository.CompleteAsync(
                    first,
                    firstResult.StorageKey,
                    firstResult.RowCount,
                    firstResult.ColumnCount,
                    cancellationToken
                )
            );

            var nextVersion = new DatasetVersion(
                seed.Dataset.Id,
                2,
                "source.csv"
            );
            var nextJob = new ImportJob(
                nextVersion.Id,
                "source.csv",
                seed.Job.SourceStorageKey
            );
            context.AddRange(nextVersion, nextJob);
            await context.SaveChangesAsync(cancellationToken);
            var second = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );

            using var scope = _fixture.Services.CreateScope();
            var secondResult = await scope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>().ImportAsync(
                nextJob.SourceStorageKey,
                $"datasets/{seed.Dataset.Id:N}/versions/{nextVersion.Id:N}/attempts",
                cancellationToken
            );
            Assert.Equal(
                ImportJobCompletionOutcome.Completed,
                await repository.CompleteAsync(
                    second,
                    secondResult.StorageKey,
                    secondResult.RowCount,
                    secondResult.ColumnCount,
                    cancellationToken
                )
            );
            var before = await ReadStateAsync(seed, cancellationToken);

            Assert.Equal(nextVersion.Id, before.CurrentVersionId);

            Assert.Equal(
                ImportJobCompletionOutcome.AlreadyCompleted,
                await repository.CompleteAsync(
                    first,
                    firstResult.StorageKey,
                    firstResult.RowCount,
                    firstResult.ColumnCount,
                    cancellationToken
                )
            );

            AssertStateEqual(before, await ReadStateAsync(seed, cancellationToken));
            Assert.True(await repository.IsCompletedAsync(
                first,
                firstResult.StorageKey,
                firstResult.RowCount,
                firstResult.ColumnCount,
                cancellationToken
            ));
            Assert.False(await repository.IsCompletedAsync(
                first,
                secondResult.StorageKey,
                firstResult.RowCount,
                firstResult.ColumnCount,
                cancellationToken
            ));
            Assert.True(await ResultExistsAsync(firstResult, cancellationToken));
            Assert.True(await ResultExistsAsync(secondResult, cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    private static async Task<bool> FinalizeAttemptAsync(
        ImportJobRepository repository,
        ImportJobWorkItem attempt,
        DuckDbCsvImportResult result,
        bool failImport,
        CancellationToken cancellationToken)
    {
        if (failImport)
        {
            return await repository.TryFailAsync(
                attempt,
                "Owned failure",
                cancellationToken
            );
        }

        return await repository.CompleteAsync(
            attempt,
            result.StorageKey,
            result.RowCount,
            result.ColumnCount,
            cancellationToken
        ) == ImportJobCompletionOutcome.Completed;
    }

    private static async Task DeleteResourceAsync(
        AppDbContext context,
        Seed seed,
        bool deleteProject,
        CancellationToken cancellationToken)
    {
        var cleanupQueue = new StorageCleanupQueue(context);

        if (deleteProject)
        {
            Assert.Equal(
                ProjectDeletionOutcome.Deleted,
                await new ProjectDeletionStore(context, cleanupQueue).DeleteAsync(
                    seed.Project.Id,
                    seed.Project.OwnerId,
                    cancellationToken
                )
            );
        }
        else
        {
            Assert.Equal(
                DatasetDeletionOutcome.Deleted,
                await new DatasetDeletionStore(
                    context,
                    cleanupQueue,
                    new ProjectWriteTransactionFactory(context)
                ).DeleteAsync(
                    seed.Dataset.Id,
                    seed.Project.Id,
                    seed.Project.OwnerId,
                    cancellationToken
                )
            );
        }
    }

    private static Task<int> ChangeAttemptTokenAsync(
        AppDbContext context,
        Guid jobId,
        long attemptNumber,
        CancellationToken cancellationToken)
    {
        return context.ImportJobs
            .Where(job =>
                job.Id == jobId
                && job.AttemptNumber == attemptNumber
                && job.Status == ImportJobStatus.Running
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    job => job.AttemptNumber,
                    job => job.AttemptNumber + 1
                ),
                cancellationToken
            );
    }

    private static void AssertStateEqual(State expected, State actual)
    {
        Assert.Equal(expected.JobStatus, actual.JobStatus);
        Assert.Equal(expected.AttemptNumber, actual.AttemptNumber);
        Assert.Equal(expected.StartedAtUtc, actual.StartedAtUtc);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.Equal(expected.ErrorMessage, actual.ErrorMessage);
        Assert.Equal(expected.VersionStatus, actual.VersionStatus);
        Assert.Equal(expected.StorageKey, actual.StorageKey);
        Assert.Equal(expected.RowCount, actual.RowCount);
        Assert.Equal(expected.ColumnCount, actual.ColumnCount);
        Assert.Equal(expected.CurrentVersionId, actual.CurrentVersionId);
    }

    private AppDbContext CreateContext(IInterceptor? interceptor = null)
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
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        var project = new Project(
            Guid.NewGuid(),
            "Fenced import project"
        );
        var dataset = new Dataset(
            project.Id,
            "Fenced dataset"
        );
        var version = new DatasetVersion(
            dataset.Id,
            1,
            "source.csv"
        );
        var sourceKey = $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv";
        var job = new ImportJob(
            version.Id,
            "source.csv",
            sourceKey
        );

        await using var context = CreateContext();
        context.AddRange(project, dataset, version, job);
        await context.SaveChangesAsync(cancellationToken);

        using var scope = _fixture.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());
        await storage.SaveAsync(sourceKey, source, cancellationToken);

        return new Seed(
            project,
            dataset,
            version,
            job
        );
    }

    private async Task<DuckDbCsvImportResult> CreateResultAsync(
        Seed seed,
        CancellationToken cancellationToken)
    {
        using var scope = _fixture.Services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>();
        return await importer.ImportAsync(
            seed.Job.SourceStorageKey,
            $"datasets/{seed.Dataset.Id:N}/versions/{seed.Version.Id:N}/attempts",
            cancellationToken
        );
    }

    private async Task<bool> ResultExistsAsync(
        DuckDbCsvImportResult result,
        CancellationToken cancellationToken)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IFileStorage>().ExistsAsync(
            result.StorageKey,
            cancellationToken
        );
    }

    private async Task<State> ReadStateAsync(Seed seed, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var job = await context.ImportJobs
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Job.Id, cancellationToken);

        var version = await context.DatasetVersions
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Version.Id, cancellationToken);

        var dataset = await context.Datasets
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.Dataset.Id, cancellationToken);

        return new State(
            job.Status,
            job.AttemptNumber,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.ErrorMessage,
            version.Status,
            version.StorageKey,
            version.RowCount,
            version.ColumnCount,
            dataset.CurrentVersionId
        );
    }

    private static async Task<ImportJobWorkItem?> RequeueAndClaimAsync(
        AppDbContext context,
        Seed seed,
        long expectedAttempt,
        CancellationToken cancellationToken)
    {
        // Model the future lease handoff in test SQL; production still only claims Pending jobs.
        await using (var transaction = await new ProjectWriteTransactionFactory(context).BeginAsync(
            seed.Project.Id,
            cancellationToken
        ))
        {
            Assert.NotNull(transaction);
            var changed = await context.ImportJobs
                .Where(job =>
                    job.Id == seed.Job.Id
                    && job.AttemptNumber == expectedAttempt
                    && job.Status == ImportJobStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(job => job.Status, ImportJobStatus.Pending),
                    cancellationToken
                );
            await transaction.CommitAsync(cancellationToken);
            if (changed == 0)
            {
                return null;
            }
        }

        return await CreateRepository(context).ClaimNextPendingAsync(cancellationToken);
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var context = CreateContext();
        await context.Projects
            .Where(item => item.Id == seed.Project.Id)
            .ExecuteDeleteAsync(CancellationToken.None);

        await context.StorageCleanupJobs
            .Where(item => item.DatasetId == seed.Dataset.Id)
            .ExecuteDeleteAsync(CancellationToken.None);

        using var scope = _fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFileStorage>().DeleteDirectoryAsync(
            $"datasets/{seed.Dataset.Id:N}",
            CancellationToken.None
        );
    }

    private sealed record Seed(
        Project Project,
        Dataset Dataset,
        DatasetVersion Version,
        ImportJob Job
    );

    private sealed record State(
        ImportJobStatus JobStatus,
        long AttemptNumber,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        string? ErrorMessage,
        DatasetVersionStatus VersionStatus,
        string? StorageKey,
        int? RowCount,
        int? ColumnCount,
        Guid? CurrentVersionId
    );

    private sealed class SqlGate : DbCommandInterceptor
    {
        private readonly string _marker;

        public SqlGate(string marker)
        {
            _marker = marker;
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
            }
            return result;
        }
    }

    private sealed class JobUpdateFault : DbCommandInterceptor
    {
        public int ExecutedUpdates { get; private set; }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE import_jobs", StringComparison.Ordinal))
            {
                ExecutedUpdates++;
                throw new IOException("Failure after the final database update, before commit.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class PublicationCommitFault : DbTransactionInterceptor
    {
        public int CommitCount { get; private set; }
        public bool PublicationCommitted { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            if (CommitCount == 2)
            {
                PublicationCommitted = true;
                throw new IOException("Lost acknowledgement after the actual publication commit.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ConversionGate : IFileStorage
    {
        private readonly IFileStorage _inner;
        private readonly bool _failConversion;

        public ConversionGate(IFileStorage inner, bool failConversion)
        {
            _inner = inner;
            _failConversion = failConversion;
        }

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SavedKey { get; private set; }
        public int DeleteCalls { get; private set; }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            await _inner.SaveAsync(storageKey, content, cancellationToken);
            SavedKey = storageKey;
            Reached.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            if (_failConversion)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                throw new FileNotFoundException("Controlled source read failure after ownership changed.");
            }
            return await _inner.OpenReadAsync(storageKey, cancellationToken);
        }

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return _inner.DeleteAsync(storageKey, cancellationToken);
        }

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
        }
    }
}
