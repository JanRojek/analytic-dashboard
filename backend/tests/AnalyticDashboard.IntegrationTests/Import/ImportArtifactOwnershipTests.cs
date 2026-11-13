using System.Collections.Concurrent;
using System.Text;

using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Services.Import;
using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class ImportArtifactOwnershipTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ImportArtifactOwnershipTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_ShouldPreserveExistingArtifact_WhenSourceOrDestinationFails(bool missingSource)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var directory = $"tests/import-ownership/{Guid.NewGuid():N}";
        var sourceKey = $"{directory}/source.csv";
        var occupiedPath = $"{directory}/published.parquet";
        var guarded = new ObservedStorage(storage);
        var importer = new DuckDbCsvImporter(guarded, NullLogger<DuckDbCsvImporter>.Instance);
        var original = "published artifact must remain unchanged"u8.ToArray();
        try
        {
            await using var published = new MemoryStream(original);
            await storage.SaveAsync(occupiedPath, published, CancellationToken);
            if (!missingSource)
            {
                await using var source = new MemoryStream("Country,Amount\nPL,10\n"u8.ToArray());
                await storage.SaveAsync(sourceKey, source, CancellationToken);
            }

            await Assert.ThrowsAnyAsync<IOException>(() => importer.ImportAsync(sourceKey, occupiedPath, CancellationToken));

            Assert.Equal(0, guarded.DeleteCalls);
            await using var saved = await storage.OpenReadAsync(occupiedPath, CancellationToken);
            using var contents = new MemoryStream();
            await saved.CopyToAsync(contents, CancellationToken);
            Assert.Equal(original, contents.ToArray());
        }
        finally
        {
            await storage.DeleteDirectoryAsync(directory, CancellationToken.None);
        }
    }

    [Fact]
    public async Task TwoAttemptsInSameJobNamespace_ShouldWriteDifferentParquetFiles()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var scope = _fixture.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var directory = $"tests/import-ownership/{Guid.NewGuid():N}";
        var sourceKey = $"{directory}/source.csv";
        var resultDirectory = $"{directory}/same-job/attempts";
        var guarded = new ObservedStorage(storage, resultDirectory + "/");
        var firstImporter = new DuckDbCsvImporter(guarded, NullLogger<DuckDbCsvImporter>.Instance);
        var secondImporter = new DuckDbCsvImporter(guarded, NullLogger<DuckDbCsvImporter>.Instance);
        try
        {
            await using var source = new MemoryStream("Country,Amount\nPL,10\n"u8.ToArray());
            await storage.SaveAsync(sourceKey, source, timeout.Token);

            var first = firstImporter.ImportAsync(sourceKey, resultDirectory, timeout.Token);
            var second = secondImporter.ImportAsync(sourceKey, resultDirectory, timeout.Token);
            var results = await Task.WhenAll(first, second).WaitAsync(timeout.Token);

            Assert.NotEqual(results[0].StorageKey, results[1].StorageKey);
            Assert.Equal(2, guarded.SavedKeys.Distinct().Count());
            foreach (var result in results)
            {
                Assert.StartsWith(resultDirectory + "/", result.StorageKey);
                await using var saved = await storage.OpenReadAsync(result.StorageKey, timeout.Token);
                var magic = new byte[4];
                await saved.ReadExactlyAsync(magic, timeout.Token);
                Assert.Equal("PAR1", Encoding.ASCII.GetString(magic));
                Assert.Equal(1, result.RowCount);
            }
        }
        finally
        {
            await storage.DeleteDirectoryAsync(directory, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFinalization_ShouldPreserveArtifactsEvenWhenCommitSucceeded_AndUseExistingDatasetCleanup(
        bool commitSucceeded)
    {
        var project = new Project(Guid.NewGuid(), "Artifact publication project");
        var dataset = new Dataset(project.Id, "Dataset");
        var previous = new DatasetVersion(dataset.Id, 1, "previous.csv");
        var next = new DatasetVersion(dataset.Id, 2, "next.csv");
        var sourceKey = $"datasets/{dataset.Id:N}/imports/{next.Id:N}/source.csv";
        var job = new ImportJob(next.Id, "next.csv", sourceKey);
        await using var seedScope = _fixture.Services.CreateAsyncScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = seedScope.ServiceProvider.GetRequiredService<IFileStorage>();
        var originalImporter = seedScope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>();
        try
        {
            await using var source = new MemoryStream("Country,Amount\nPL,10\n"u8.ToArray());
            await storage.SaveAsync(sourceKey, source, CancellationToken);
            var published = await originalImporter.ImportAsync(
                sourceKey,
                $"datasets/{dataset.Id:N}/versions/{previous.Id:N}/attempts",
                CancellationToken
            );
            seedContext.AddRange(project, dataset, previous, next, job);
            await seedContext.SaveChangesAsync(CancellationToken);
            previous.MarkReady(published.StorageKey, published.RowCount, published.ColumnCount);
            dataset.PublishVersion(previous.Id);
            await seedContext.SaveChangesAsync(CancellationToken);
            var guarded = new ObservedStorage(storage);

            await using (var processingScope = _fixture.Services.CreateAsyncScope())
            {
                var repository = processingScope.ServiceProvider.GetRequiredService<IImportJobRepository>();
                var processor = new ImportJobProcessor(
                    new FailingFinalizationRepository(repository, commitSucceeded),
                    new DuckDbCsvImporter(guarded, NullLogger<DuckDbCsvImporter>.Instance),
                    NullLogger<ImportJobProcessor>.Instance
                );

                await Assert.ThrowsAsync<IOException>(() => processor.ProcessNextAsync(CancellationToken));
            }

            Assert.Equal(0, guarded.DeleteCalls);
            var attemptKey = Assert.Single(guarded.SavedKeys);
            Assert.NotEqual(published.StorageKey, attemptKey);
            Assert.True(await storage.ExistsAsync(published.StorageKey, CancellationToken));
            Assert.True(await storage.ExistsAsync(attemptKey, CancellationToken));
            await using var verificationScope = _fixture.Services.CreateAsyncScope();
            var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedNext = await verification.DatasetVersions.AsNoTracking()
                .SingleAsync(item => item.Id == next.Id, CancellationToken);
            var savedDataset = await verification.Datasets.AsNoTracking()
                .SingleAsync(item => item.Id == dataset.Id, CancellationToken);
            Assert.Equal(commitSucceeded ? next.Id : previous.Id, savedDataset.CurrentVersionId);
            Assert.Equal(commitSucceeded ? DatasetVersionStatus.Ready : DatasetVersionStatus.Pending, savedNext.Status);
            Assert.Equal(commitSucceeded ? attemptKey : null, savedNext.StorageKey);
            if (!commitSucceeded)
            {
                var recovery = verificationScope.ServiceProvider.GetRequiredService<ImportJobRecoveryService>();
                await recovery.RecoverInterruptedAsync(CancellationToken);
                Assert.True(await storage.ExistsAsync(published.StorageKey, CancellationToken));
                Assert.True(await storage.ExistsAsync(attemptKey, CancellationToken));
            }

            var deletion = verificationScope.ServiceProvider.GetRequiredService<IDatasetDeletionStore>();
            Assert.Equal(DatasetDeletionOutcome.Deleted, await deletion.DeleteAsync(
                dataset.Id,
                project.Id,
                project.OwnerId,
                CancellationToken
            ));
            Assert.True(await verification.StorageCleanupJobs.AnyAsync(item => item.DatasetId == dataset.Id, CancellationToken));
            var cleanup = verificationScope.ServiceProvider.GetRequiredService<StorageCleanupProcessor>();
            Assert.True(await cleanup.ProcessNextAsync(CancellationToken));
            Assert.False(await storage.ExistsAsync(published.StorageKey, CancellationToken));
            Assert.False(await storage.ExistsAsync(attemptKey, CancellationToken));
            Assert.False(await storage.ExistsAsync(sourceKey, CancellationToken));
        }
        finally
        {
            await storage.DeleteDirectoryAsync($"datasets/{dataset.Id:N}", CancellationToken.None);
        }
    }

    [Fact]
    public async Task DatasetCleanupFailure_ShouldNotDeleteAnotherDatasetsPublishedAttempt()
    {
        var project = new Project(Guid.NewGuid(), "Cleanup ownership project");
        var publishedDataset = new Dataset(project.Id, "Published dataset");
        var removedDataset = new Dataset(project.Id, "Removed dataset");
        var version = new DatasetVersion(publishedDataset.Id, 1, "source.csv");
        var publishedKey = $"datasets/{publishedDataset.Id:N}/versions/{version.Id:N}/attempts/{Guid.NewGuid():N}.parquet";
        var removedKey = $"datasets/{removedDataset.Id:N}/versions/{Guid.NewGuid():N}/attempts/{Guid.NewGuid():N}.parquet";
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        try
        {
            await using var published = new MemoryStream("published data"u8.ToArray());
            await using var removed = new MemoryStream("unpublished data"u8.ToArray());
            await storage.SaveAsync(publishedKey, published, CancellationToken);
            await storage.SaveAsync(removedKey, removed, CancellationToken);
            context.AddRange(project, publishedDataset, removedDataset, version);
            await context.SaveChangesAsync(CancellationToken);
            version.MarkReady(publishedKey, 1, 1);
            publishedDataset.PublishVersion(version.Id);
            await context.SaveChangesAsync(CancellationToken);
            var deletion = scope.ServiceProvider.GetRequiredService<IDatasetDeletionStore>();
            Assert.Equal(DatasetDeletionOutcome.Deleted, await deletion.DeleteAsync(
                removedDataset.Id,
                project.Id,
                project.OwnerId,
                CancellationToken
            ));
            var guarded = new ObservedStorage(storage);
            var cleanup = new StorageCleanupProcessor(context, guarded, NullLogger<StorageCleanupProcessor>.Instance);

            Assert.True(await cleanup.ProcessNextAsync(CancellationToken));

            var queued = await context.StorageCleanupJobs.SingleAsync(
                item => item.DatasetId == removedDataset.Id,
                CancellationToken
            );
            Assert.Equal(1, queued.AttemptCount);
            Assert.True(await storage.ExistsAsync(publishedKey, CancellationToken));
            Assert.True(await storage.ExistsAsync(removedKey, CancellationToken));
            Assert.Equal(DatasetVersionStatus.Ready, (await context.DatasetVersions.AsNoTracking()
                .SingleAsync(item => item.Id == version.Id, CancellationToken)).Status);
            context.Entry(queued).Property(item => item.NextAttemptAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync(CancellationToken);
            guarded.AllowDirectoryCleanup = true;

            Assert.True(await cleanup.ProcessNextAsync(CancellationToken));

            Assert.True(await storage.ExistsAsync(publishedKey, CancellationToken));
            Assert.False(await storage.ExistsAsync(removedKey, CancellationToken));
            await using var saved = await storage.OpenReadAsync(publishedKey, CancellationToken);
            using var reader = new StreamReader(saved, Encoding.UTF8);
            Assert.Equal("published data", await reader.ReadToEndAsync(CancellationToken));
        }
        finally
        {
            await storage.DeleteDirectoryAsync($"datasets/{publishedDataset.Id:N}", CancellationToken.None);
            await storage.DeleteDirectoryAsync($"datasets/{removedDataset.Id:N}", CancellationToken.None);
        }
    }

    private sealed class ObservedStorage : IFileStorage
    {
        private readonly IFileStorage _inner;
        private readonly string? _barrierPrefix;
        private readonly TaskCompletionSource _bothSaves = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private int _deleteCalls;

        public ObservedStorage(IFileStorage inner, string? barrierPrefix = null)
        {
            _inner = inner;
            _barrierPrefix = barrierPrefix;
        }

        public ConcurrentQueue<string> SavedKeys { get; } = new();
        public int DeleteCalls => _deleteCalls;
        public bool AllowDirectoryCleanup { get; set; }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            SavedKeys.Enqueue(storageKey);
            if (_barrierPrefix is not null && storageKey.StartsWith(_barrierPrefix, StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _arrived) == 2)
                {
                    _bothSaves.SetResult();
                }
                await _bothSaves.Task.WaitAsync(cancellationToken);
            }
            await _inner.SaveAsync(storageKey, content, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(storageKey, cancellationToken);

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _deleteCalls);
            throw new IOException("Cleanup unavailable; import must not attempt to delete published or unowned files.");
        }

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            AllowDirectoryCleanup
                ? _inner.DeleteDirectoryAsync(storageKey, cancellationToken)
                : throw new IOException("Directory cleanup is unavailable.");
    }

    private sealed class FailingFinalizationRepository : IImportJobRepository
    {
        private readonly IImportJobRepository _inner;
        private readonly bool _commitSucceeded;

        public FailingFinalizationRepository(IImportJobRepository inner, bool commitSucceeded)
        {
            _inner = inner;
            _commitSucceeded = commitSucceeded;
        }

        public Task<ImportJobWorkItem?> ClaimNextPendingAsync(CancellationToken cancellationToken) =>
            _inner.ClaimNextPendingAsync(cancellationToken);

        public Task<ImportJob?> GetLatestByDatasetIdAsync(Guid datasetId, CancellationToken cancellationToken) =>
            _inner.GetLatestByDatasetIdAsync(datasetId, cancellationToken);

        public Task<IReadOnlyList<ImportJob>> GetRunningAsync(CancellationToken cancellationToken) =>
            _inner.GetRunningAsync(cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (_commitSucceeded)
            {
                await _inner.SaveChangesAsync(cancellationToken);
            }
            throw new IOException("Simulated loss of database acknowledgement.");
        }
    }
}
