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

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class ImportStorageLifecycleTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ImportStorageLifecycleTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LateArtifact_ShouldBeRejected_AfterNamespaceClosureAndCleanup(
        bool deleteProject,
        bool finishConversionAfterDeletion)
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var seed = await SeedAsync(token);
        var datasetDirectory = Path.Combine("storage", "datasets", seed.Dataset.Id.ToString("N"));
        var storage = new DelayedStorage(
            new LocalFileStorage(),
            finishConversionAfterDeletion,
            token
        );
        await using var processingContext = CreateContext();
        var options = Options.Create(new ImportJobOptions
        {
            MaxAttempts = 1,
            LeaseDuration = TimeSpan.FromMinutes(5),
            HeartbeatInterval = TimeSpan.FromMinutes(1)
        });
        var processor = new ImportJobProcessor(
            CreateRepository(processingContext, options),
            new DuckDbCsvImporter(
                storage,
                NullLogger<DuckDbCsvImporter>.Instance,
                options
            ),
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            options,
            TimeProvider.System,
            NullLogger<ImportJobProcessor>.Instance
        );
        Task<bool>? processing = null;

        try
        {
            processing = processor.ProcessNextAsync(token);
            await storage.Reached.Task.WaitAsync(token);

            await using (var recoveryContext = CreateContext())
            {
                Assert.Equal(1, await recoveryContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE import_jobs SET "LeaseExpiresAtUtc" = '-infinity'
                    WHERE "Id" = {seed.Job.Id} AND "Status" = 'Running' AND "AttemptNumber" = 1
                    """, token));
                Assert.True(await CreateRepository(recoveryContext, options).RecoverNextExpiredAsync(token));
                var failed = await recoveryContext.ImportJobs.AsNoTracking()
                    .SingleAsync(item => item.Id == seed.Job.Id, token);
                Assert.Equal(ImportJobStatus.Failed, failed.Status);
                Assert.Equal(1L, failed.AttemptNumber);
            }

            await DeleteAsync(seed, deleteProject, token);
            Assert.True(await ProcessCleanupAsync(token));
            Assert.False(Directory.Exists(datasetDirectory));
            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));
            Assert.Equal(
                LocalStorageNamespaceState.Closed,
                await new LocalFileStorage().GetNamespaceStateAsync(seed.Dataset.Id, token)
            );

            storage.Release.TrySetResult();
            Assert.True(await processing.WaitAsync(token));

            var lateKey = Assert.IsType<string>(storage.SavedKey);
            Assert.True(storage.InstallationRejected);
            Assert.False(await storage.ExistsAsync(lateKey, token));
            Assert.False(Directory.Exists(datasetDirectory));
            await using (var observer = CreateContext())
            {
                Assert.False(await observer.Datasets.AnyAsync(item => item.Id == seed.Dataset.Id, token));
                Assert.False(await observer.DatasetVersions.AnyAsync(item => item.Id == seed.Version.Id, token));
                Assert.False(await observer.ImportJobs.AnyAsync(item => item.Id == seed.Job.Id, token));
                Assert.Equal(!deleteProject, await observer.Projects.AnyAsync(item => item.Id == seed.Project.Id, token));
                Assert.False(await observer.StorageCleanupJobs.AnyAsync(
                    item => item.DatasetId == seed.Dataset.Id,
                    token
                ));
            }

            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));
            Assert.False(await ProcessCleanupAsync(token));
        }
        finally
        {
            storage.Release.TrySetResult();
            try
            {
                if (processing is not null)
                {
                    await processing.WaitAsync(token);
                }
            }
            finally
            {
                await CleanupAsync(seed);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_ShouldBeSafeToRetry_WhenDatabaseCompletionFails(bool commitSucceeded)
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var seed = await SeedAsync(token);
        var storage = new LocalFileStorage();

        try
        {
            await using (var failingContext = CreateContext())
            {
                var repository = CreateRepository(failingContext, Options.Create(new ImportJobOptions()));
                var attempt = Assert.IsType<ImportJobWorkItem>(await repository.ClaimNextPendingAsync(token));
                Assert.True(await repository.TryFailAsync(attempt, "Invalid CSV.", token));
            }
            await DeleteAsync(seed, deleteProject: false, token);

            var fault = new CleanupAcknowledgementFault(commitSucceeded);
            await using (var cleaningContext = CreateContext(fault))
            {
                var processor = new StorageCleanupProcessor(
                    cleaningContext,
                    storage,
                    storage,
                    NullLogger<StorageCleanupProcessor>.Instance
                );

                var exception = await Assert.ThrowsAsync<IOException>(() => processor.ProcessNextAsync(token));

                Assert.Same(fault.Failure, exception);
                Assert.Equal(commitSucceeded ? 1 : 0, fault.DeletedRows);
            }
            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));
            Assert.Equal(
                LocalStorageNamespaceState.Closed,
                await storage.GetNamespaceStateAsync(seed.Dataset.Id, token)
            );

            var lateKey = $"datasets/{seed.Dataset.Id:N}/versions/{seed.Version.Id:N}/attempts/{Guid.NewGuid():N}.parquet";
            await using (var late = new MemoryStream("unpublished late artifact"u8.ToArray()))
            {
                await Assert.ThrowsAsync<StorageNamespaceClosedException>(
                    () => storage.SaveAsync(lateKey, late, token)
                );
            }
            await using (var observer = CreateContext())
            {
                Assert.Equal(!commitSucceeded, await observer.StorageCleanupJobs.AnyAsync(
                    item => item.DatasetId == seed.Dataset.Id,
                    token
                ));
            }

            Assert.Equal(!commitSucceeded, await ProcessCleanupAsync(token));

            Assert.False(await storage.ExistsAsync(lateKey, token));
            Assert.False(await ProcessCleanupAsync(token));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Cleanup_ShouldCompleteIdempotently_WhenTwoProcessorsHandleTheSameJob()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var seed = await SeedAsync(token);
        var gate = new CleanupCompletionGate();
        await using var firstContext = CreateContext(gate);
        var storage = new LocalFileStorage();
        Task<bool>? firstCleanup = null;

        try
        {
            await using (var failingContext = CreateContext())
            {
                var repository = CreateRepository(failingContext, Options.Create(new ImportJobOptions()));
                var attempt = Assert.IsType<ImportJobWorkItem>(await repository.ClaimNextPendingAsync(token));
                Assert.True(await repository.TryFailAsync(attempt, "Invalid CSV.", token));
            }
            await DeleteAsync(seed, deleteProject: false, token);
            var processor = new StorageCleanupProcessor(
                firstContext,
                storage,
                storage,
                NullLogger<StorageCleanupProcessor>.Instance
            );

            firstCleanup = processor.ProcessNextAsync(token);
            await gate.Reached.Task.WaitAsync(token);
            Assert.True(await ProcessCleanupAsync(token));
            gate.Release.TrySetResult();
            Assert.True(await firstCleanup.WaitAsync(token));

            Assert.Equal(0, gate.DeletedRows);
            Assert.False(await ProcessCleanupAsync(token));
            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));
            Assert.Equal(
                LocalStorageNamespaceState.Closed,
                await storage.GetNamespaceStateAsync(seed.Dataset.Id, token)
            );
        }
        finally
        {
            gate.Release.TrySetResult();
            try
            {
                if (firstCleanup is not null)
                {
                    await firstCleanup.WaitAsync(token);
                }
            }
            finally
            {
                await CleanupAsync(seed);
            }
        }
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        var project = new Project(
            Guid.NewGuid(),
            "Import storage lifecycle"
        );
        var dataset = new Dataset(
            project.Id,
            "Lifecycle dataset"
        );
        var version = new DatasetVersion(
            dataset.Id,
            1,
            "source.csv"
        );
        var job = new ImportJob(
            version.Id,
            version.OriginalFileName,
            $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
        );

        await using var context = CreateContext();
        context.AddRange(project, dataset, version, job);
        await context.SaveChangesAsync(cancellationToken);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());
        await new LocalFileStorage().SaveAsync(job.SourceStorageKey, source, cancellationToken);

        return new Seed(project, dataset, version, job);
    }

    private async Task DeleteAsync(Seed seed, bool deleteProject, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var queue = new StorageCleanupQueue(context);
        if (deleteProject)
        {
            Assert.Equal(
                ProjectDeletionOutcome.Deleted,
                await new ProjectDeletionStore(context, queue).DeleteAsync(
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
                    queue,
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

    private async Task<bool> ProcessCleanupAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var storage = new LocalFileStorage();
        return await new StorageCleanupProcessor(
            context,
            storage,
            storage,
            NullLogger<StorageCleanupProcessor>.Instance
        ).ProcessNextAsync(cancellationToken);
    }

    private AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        using var scope = _fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.GetConnectionString();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options);
    }

    private static ImportJobRepository CreateRepository(
        AppDbContext context,
        IOptions<ImportJobOptions> options)
    {
        return new ImportJobRepository(
            context,
            new ProjectWriteTransactionFactory(context),
            options,
            NullLogger<ImportJobRepository>.Instance
        );
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var context = CreateContext();
        await context.Projects.Where(item => item.Id == seed.Project.Id)
            .ExecuteDeleteAsync(CancellationToken.None);
        await context.StorageCleanupJobs.Where(item => item.DatasetId == seed.Dataset.Id)
            .ExecuteDeleteAsync(CancellationToken.None);
        await new LocalFileStorage().DeleteDirectoryAsync(
            $"datasets/{seed.Dataset.Id:N}",
            CancellationToken.None
        );
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private sealed record Seed(Project Project, Dataset Dataset, DatasetVersion Version, ImportJob Job);

    private sealed class DelayedStorage : IFileStorage
    {
        private readonly LocalFileStorage _inner;
        private readonly bool _delayConversion;
        private readonly CancellationToken _testToken;

        public DelayedStorage(
            LocalFileStorage inner,
            bool delayConversion,
            CancellationToken testToken)
        {
            _inner = inner;
            _delayConversion = delayConversion;
            _testToken = testToken;
        }

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SavedKey { get; private set; }
        public bool InstallationRejected { get; private set; }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            SavedKey = storageKey;
            await using var staged = await _inner.StageAsync(content, _testToken);
            if (!_delayConversion)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(_testToken);
            }
            try
            {
                await _inner.InstallAsync(staged, storageKey, _testToken);
            }
            catch (StorageNamespaceClosedException)
            {
                InstallationRejected = true;
                throw;
            }
        }

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            var source = await _inner.OpenReadAsync(storageKey, cancellationToken);
            if (!_delayConversion)
            {
                return source;
            }

            var buffered = new MemoryStream();
            try
            {
                await using (source)
                {
                    await source.CopyToAsync(buffered, cancellationToken);
                }
                buffered.Position = 0;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(_testToken);
                return buffered;
            }
            catch
            {
                await buffered.DisposeAsync();
                throw;
            }
        }

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(storageKey, cancellationToken);

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
    }

    private sealed class CleanupAcknowledgementFault(bool commitSucceeded) : DbCommandInterceptor
    {
        public IOException Failure { get; } = new("Cleanup database completion failed.");
        public int DeletedRows { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!commitSucceeded && IsCleanupCompletion(command))
            {
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (IsCleanupCompletion(command))
            {
                DeletedRows = result;
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CleanupCompletionGate : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DeletedRows { get; private set; } = -1;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCleanupCompletion(command))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (IsCleanupCompletion(command))
            {
                DeletedRows = result;
            }
            return ValueTask.FromResult(result);
        }
    }

    private static bool IsCleanupCompletion(DbCommand command) =>
        command.CommandText.StartsWith("DELETE FROM storage_cleanup_jobs", StringComparison.Ordinal);
}
