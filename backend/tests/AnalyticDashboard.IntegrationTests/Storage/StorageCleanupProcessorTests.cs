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
    public async Task ProcessNextAsync_ShouldRemoveJob_WhenStorageDeletionSucceeds()
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
            NullLogger<StorageCleanupProcessor>.Instance
        );

        var processed = await processor.ProcessNextAsync(CancellationToken);

        Assert.True(processed);
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
    }

    [Fact]
    public async Task ProcessNextAsync_ShouldScheduleRetry_WhenStorageDeletionFails()
    {
        var job = new StorageCleanupJob(Guid.NewGuid());
        var storage = new TestFileStorage
        {
            ThrowOnDelete = true
        };

        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.StorageCleanupJobs.Add(job);
        await context.SaveChangesAsync(CancellationToken);

        var processor = new StorageCleanupProcessor(
            context,
            storage,
            NullLogger<StorageCleanupProcessor>.Instance
        );

        var processed = await processor.ProcessNextAsync(CancellationToken);

        Assert.True(processed);
        Assert.Equal(1, job.AttemptCount);
        Assert.True(job.NextAttemptAtUtc > DateTime.UtcNow);

        Assert.Equal(
            $"datasets/{job.DatasetId:N}",
            Assert.Single(storage.DeletedDirectories)
        );

        Assert.True(await context.StorageCleanupJobs
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == job.Id,
                CancellationToken
            ));

        var processedAgain = await processor.ProcessNextAsync(CancellationToken);

        Assert.False(processedAgain);
        Assert.Single(storage.DeletedDirectories);
    }

    private sealed class TestFileStorage : IFileStorage
    {
        public List<string> DeletedDirectories { get; } = [];

        public bool ThrowOnDelete { get; set; }

        public Task DeleteDirectoryAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
