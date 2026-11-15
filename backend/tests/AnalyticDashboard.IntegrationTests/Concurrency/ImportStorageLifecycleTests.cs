using System.Text;

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
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public async Task LateArtifact_ShouldRemainCoveredByDeletionIntent_AfterCleanup(
        bool deleteProject,
        bool finishConversionAfterDeletion,
        bool loseWriteAcknowledgement)
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var seed = await SeedAsync(token);
        var storage = new DelayedStorage(
            new LocalFileStorage(),
            finishConversionAfterDeletion,
            loseWriteAcknowledgement,
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
            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));

            storage.Release.TrySetResult();
            Assert.True(await processing.WaitAsync(token));

            var lateKey = Assert.IsType<string>(storage.SavedKey);
            Assert.True(await storage.ExistsAsync(lateKey, token));
            await using (var late = await storage.OpenReadAsync(lateKey, token))
            {
                var magic = new byte[4];
                await late.ReadExactlyAsync(magic, token);
                Assert.Equal("PAR1", Encoding.ASCII.GetString(magic));
            }
            await using (var observer = CreateContext())
            {
                Assert.False(await observer.Datasets.AnyAsync(item => item.Id == seed.Dataset.Id, token));
                Assert.False(await observer.DatasetVersions.AnyAsync(item => item.Id == seed.Version.Id, token));
                Assert.False(await observer.ImportJobs.AnyAsync(item => item.Id == seed.Job.Id, token));
                Assert.Equal(!deleteProject, await observer.Projects.AnyAsync(item => item.Id == seed.Project.Id, token));
                Assert.True(await observer.StorageCleanupJobs.AnyAsync(
                    item => item.DatasetId == seed.Dataset.Id,
                    token
                ));
            }

            await MakeCleanupDueAsync(seed.Dataset.Id, token);
            Assert.True(await ProcessCleanupAsync(token));

            Assert.False(await storage.ExistsAsync(lateKey, token));
            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));
            Assert.False(await ProcessCleanupAsync(token));

            await MakeCleanupDueAsync(seed.Dataset.Id, token);
            Assert.True(await ProcessCleanupAsync(token));
            Assert.False(await storage.ExistsAsync(lateKey, token));
            await using var final = CreateContext();
            var intent = await final.StorageCleanupJobs.SingleAsync(
                item => item.DatasetId == seed.Dataset.Id,
                token
            );
            Assert.Equal(0, intent.AttemptCount);
            Assert.True(intent.NextAttemptAtUtc > DateTime.UtcNow);
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

    [Fact]
    public async Task Cleanup_ShouldKeepItsIntent_WhenAcknowledgementIsLostAfterTheActualDatabaseWrite()
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

            var fault = new CleanupAcknowledgementFault();
            await using (var cleaningContext = CreateContext(fault))
            {
                var processor = new StorageCleanupProcessor(
                    cleaningContext,
                    storage,
                    NullLogger<StorageCleanupProcessor>.Instance
                );

                var exception = await Assert.ThrowsAsync<IOException>(() => processor.ProcessNextAsync(token));

                Assert.Same(fault.Failure, exception);
                Assert.Equal(1, fault.SavedCalls);
            }
            Assert.False(await storage.ExistsAsync(seed.Job.SourceStorageKey, token));

            var lateKey = $"datasets/{seed.Dataset.Id:N}/versions/{seed.Version.Id:N}/attempts/{Guid.NewGuid():N}.parquet";
            await using (var late = new MemoryStream("unpublished late artifact"u8.ToArray()))
            {
                await storage.SaveAsync(lateKey, late, token);
            }
            await using (var observer = CreateContext())
            {
                var intent = await observer.StorageCleanupJobs.SingleAsync(
                    item => item.DatasetId == seed.Dataset.Id,
                    token
                );
                Assert.Equal(0, intent.AttemptCount);
                Assert.True(intent.NextAttemptAtUtc > DateTime.UtcNow);
            }

            await MakeCleanupDueAsync(seed.Dataset.Id, token);
            Assert.True(await ProcessCleanupAsync(token));

            Assert.False(await storage.ExistsAsync(lateKey, token));
            Assert.False(await ProcessCleanupAsync(token));
        }
        finally
        {
            await CleanupAsync(seed);
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
        return await new StorageCleanupProcessor(
            context,
            new LocalFileStorage(),
            NullLogger<StorageCleanupProcessor>.Instance
        ).ProcessNextAsync(cancellationToken);
    }

    private async Task MakeCleanupDueAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        Assert.Equal(1, await context.StorageCleanupJobs
            .Where(item => item.DatasetId == datasetId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTime.UnixEpoch),
                cancellationToken
            ));
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
        private readonly IFileStorage _inner;
        private readonly bool _delayConversion;
        private readonly bool _loseWriteAcknowledgement;
        private readonly CancellationToken _testToken;

        public DelayedStorage(
            IFileStorage inner,
            bool delayConversion,
            bool loseWriteAcknowledgement,
            CancellationToken testToken)
        {
            _inner = inner;
            _delayConversion = delayConversion;
            _loseWriteAcknowledgement = loseWriteAcknowledgement;
            _testToken = testToken;
        }

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SavedKey { get; private set; }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            SavedKey = storageKey;
            if (!_delayConversion)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(_testToken);
            }
            await _inner.SaveAsync(storageKey, content, _testToken);
            if (_loseWriteAcknowledgement)
            {
                throw new IOException("Lost acknowledgement after writing a private CSV-derived artifact.");
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

    private sealed class CleanupAcknowledgementFault : SaveChangesInterceptor
    {
        public IOException Failure { get; } = new("Lost acknowledgement after cleanup scheduling was committed.");
        public int SavedCalls { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SavedCalls++;
            throw Failure;
        }
    }
}
