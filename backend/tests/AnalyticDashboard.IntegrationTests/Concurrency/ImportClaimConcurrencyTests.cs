
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class ImportClaimConcurrencyTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ImportClaimConcurrencyTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentImportClaimAndDelete_ShouldPreserveActiveImport()
    {
        var cancellationToken = CancellationToken;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Import concurrency project");
        var dataset = new Dataset(project.Id, "Import dataset");

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

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(project, dataset, version, job);
            await context.SaveChangesAsync(cancellationToken);
        }

        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        async Task<ImportJobWorkItem?> ClaimAsync()
        {
            await startGate.Task.WaitAsync(cancellationToken);

            await using var scope = _fixture.Services.CreateAsyncScope();

            var repository = scope.ServiceProvider
                .GetRequiredService<IImportJobRepository>();

            return await repository.ClaimNextPendingAsync(cancellationToken);
        }

        async Task<DatasetDeletionOutcome> DeleteAsync()
        {
            await startGate.Task.WaitAsync(cancellationToken);

            await using var scope = _fixture.Services.CreateAsyncScope();

            var deletionStore = scope.ServiceProvider
                .GetRequiredService<IDatasetDeletionStore>();

            return await deletionStore.DeleteAsync(
                dataset.Id,
                project.Id,
                ownerId,
                cancellationToken
            );
        }

        var claimTask = ClaimAsync();
        var deleteTask = DeleteAsync();

        startGate.SetResult();

        await Task.WhenAll(claimTask, deleteTask)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        var claimedJob = Assert.IsType<ImportJobWorkItem>(
            await claimTask
        );

        Assert.Equal(job.Id, claimedJob.Job.Id);
        Assert.Equal(ImportJobStatus.Running, claimedJob.Job.Status);

        Assert.Equal(
            DatasetDeletionOutcome.ImportInProgress,
            await deleteTask
        );

        await using var verificationScope =
            _fixture.Services.CreateAsyncScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var savedDataset = await verificationContext.Datasets
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == dataset.Id,
                cancellationToken
            );

        var savedJob = await verificationContext.ImportJobs
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == job.Id,
                cancellationToken
            );

        var hasCleanupJob = await verificationContext.StorageCleanupJobs
            .AsNoTracking()
            .AnyAsync(
                item => item.DatasetId == dataset.Id,
                cancellationToken
            );

        Assert.Equal(dataset.Id, savedDataset.Id);
        Assert.Equal(ImportJobStatus.Running, savedJob.Status);
        Assert.False(hasCleanupJob);
    }
}
