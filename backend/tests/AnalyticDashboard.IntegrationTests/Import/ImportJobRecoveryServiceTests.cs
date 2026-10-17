using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class ImportJobRecoveryServiceTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ImportJobRecoveryServiceTests(
        ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RecoverInterruptedAsync_ShouldFailRunningJobAndDeleteParquet()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Import worker project"
        );

        var dataset = new Dataset(
            project.Id,
            "Sales"
        );

        var version = new DatasetVersion(
            dataset.Id,
            1,
            "sales.csv"
        );

        var sourceStorageKey = $"tests/import-jobs/{Guid.NewGuid():N}/source.csv";

        var resultStorageKey = $"datasets/{dataset.Id:N}/versions/{version.Id:N}/data.parquet";

        var importJob = new ImportJob(
            version.Id,
            "sales.csv",
            sourceStorageKey
        );

        importJob.MarkRunning();

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.Projects.Add(project);
            dbContext.Datasets.Add(dataset);
            dbContext.DatasetVersions.Add(version);
            dbContext.ImportJobs.Add(importJob);

            await dbContext.SaveChangesAsync(
                CancellationToken
            );
        }

        try
        {
            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var fileStorage = scope.ServiceProvider
                    .GetRequiredService<IFileStorage>();

                await using var parquetStream = new MemoryStream(
                    "dummy parquet content"u8.ToArray()
                );

                await fileStorage.SaveAsync(
                    resultStorageKey,
                    parquetStream,
                    CancellationToken
                );

                Assert.True(
                    await fileStorage.ExistsAsync(
                        resultStorageKey,
                        CancellationToken
                    )
                );
            }

            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var recoveryService = scope.ServiceProvider
                    .GetRequiredService<ImportJobRecoveryService>();

                await recoveryService.RecoverInterruptedAsync(
                    CancellationToken
                );
            }

            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

                var storedJob = await dbContext.ImportJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == importJob.Id, CancellationToken);

                var storedVersion = await dbContext.DatasetVersions
                    .AsNoTracking()
                    .SingleAsync(storedVersion => storedVersion.Id == version.Id, CancellationToken);

                Assert.Equal(
                    ImportJobStatus.Failed,
                    storedJob.Status
                );

                Assert.Equal(
                    DatasetVersionStatus.Failed,
                    storedVersion.Status
                );

                Assert.NotNull(
                    storedJob.CompletedAtUtc
                );

                Assert.False(
                    await fileStorage.ExistsAsync(resultStorageKey, CancellationToken)
                );
            }
        }
        finally
        {
            await using var scope = _fixture.Services.CreateAsyncScope();

            var fileStorage = scope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            await fileStorage.DeleteAsync(
                resultStorageKey,
                CancellationToken.None
            );
        }
    }
}
