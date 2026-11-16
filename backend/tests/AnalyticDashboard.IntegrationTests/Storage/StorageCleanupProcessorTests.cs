using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnalyticDashboard.IntegrationTests.Storage;

public sealed class StorageCleanupProcessorTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public StorageCleanupProcessorTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessNextAsync_ShouldCompleteJob_AfterClosingNamespaceAndDeletingStorage()
    {
        var job = new StorageCleanupJob(Guid.NewGuid());
        var storage = new TestFileStorage();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.StorageCleanupJobs.Add(job);
        await context.SaveChangesAsync(CancellationToken);

        var processor = new StorageCleanupProcessor(
            context,
            storage,
            storage,
            NullLogger<StorageCleanupProcessor>.Instance
        );

        var processed = await processor.ProcessNextAsync(CancellationToken);

        Assert.True(processed);
        Assert.Equal(
            new[] { "close", "delete" },
            storage.Operations
        );
        Assert.Equal(job.DatasetId, Assert.Single(storage.ClosedNamespaces));
        Assert.Equal(
            $"datasets/{job.DatasetId:N}",
            Assert.Single(storage.DeletedDirectories)
        );

        Assert.False(await context.StorageCleanupJobs
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == job.Id,
                CancellationToken
            ));

        Assert.False(await processor.ProcessNextAsync(CancellationToken));
        Assert.Single(storage.DeletedDirectories);
        Assert.Single(storage.ClosedNamespaces);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessNextAsync_ShouldScheduleRetry_WhenStorageCleanupFails(bool failClosure)
    {
        var job = new StorageCleanupJob(Guid.NewGuid());
        var storage = new TestFileStorage
        {
            ThrowOnClose = failClosure,
            ThrowOnDelete = !failClosure
        };

        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.StorageCleanupJobs.Add(job);
        await context.SaveChangesAsync(CancellationToken);

        var processor = new StorageCleanupProcessor(
            context,
            storage,
            storage,
            NullLogger<StorageCleanupProcessor>.Instance
        );

        var processed = await processor.ProcessNextAsync(CancellationToken);

        Assert.True(processed);
        var saved = await context.StorageCleanupJobs
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == job.Id,
                CancellationToken
            );
        Assert.Equal(1, saved.AttemptCount);
        Assert.True(saved.NextAttemptAtUtc > DateTime.UtcNow);
        Assert.Equal(job.DatasetId, Assert.Single(storage.ClosedNamespaces));

        if (failClosure)
        {
            Assert.Equal(new[] { "close" }, storage.Operations);
            Assert.Empty(storage.DeletedDirectories);
        }
        else
        {
            Assert.Equal(new[] { "close", "delete" }, storage.Operations);
            Assert.Equal(
                $"datasets/{job.DatasetId:N}",
                Assert.Single(storage.DeletedDirectories)
            );
        }

        var processedAgain = await processor.ProcessNextAsync(CancellationToken);

        Assert.False(processedAgain);
        Assert.Single(storage.ClosedNamespaces);
    }

    private sealed class TestFileStorage : IFileStorage, IStorageNamespaceCloser
    {
        public List<string> Operations { get; } = [];

        public List<Guid> ClosedNamespaces { get; } = [];

        public List<string> DeletedDirectories { get; } = [];

        public bool ThrowOnClose { get; set; }

        public bool ThrowOnDelete { get; set; }

        public Task CloseNamespaceAsync(
            Guid datasetId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Operations.Add("close");
            ClosedNamespaces.Add(datasetId);

            if (ThrowOnClose)
            {
                throw new IOException("Simulated namespace closure failure.");
            }

            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Operations.Add("delete");
            DeletedDirectories.Add(storageKey);

            if (ThrowOnDelete)
            {
                throw new IOException("Simulated storage failure.");
            }

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
