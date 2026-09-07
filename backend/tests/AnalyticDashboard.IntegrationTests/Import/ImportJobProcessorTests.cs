using System.Text;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class ImportJobProcessorTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ImportJobProcessorTests(
        ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessNextAsync_ShouldCompletePendingImportAndPublishDatasetVersion()
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

        var sourceStorageKey =
            $"tests/import-jobs/{Guid.NewGuid():N}/source.csv";

        var resultStorageKey =
            $"datasets/{dataset.Id:N}/versions/{version.Id:N}/data.parquet";

        var importJob = new ImportJob(
            version.Id,
            "sales.csv",
            sourceStorageKey
        );

        await using (var scope =
            _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            dbContext.Projects.Add(project);
            dbContext.Datasets.Add(dataset);
            dbContext.DatasetVersions.Add(version);
            dbContext.ImportJobs.Add(importJob);

            await dbContext.SaveChangesAsync(
                CancellationToken
            );
        }

        await using (var scope =
            _fixture.Services.CreateAsyncScope())
        {
            var fileStorage = scope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            var csv = """
                Name,Amount,Country
                Alice,10,Poland
                Bob,20,Germany
                Charlie,30,Poland
                """;

            await using var sourceStream =
                new MemoryStream(
                    Encoding.UTF8.GetBytes(csv)
                );

            await fileStorage.SaveAsync(
                sourceStorageKey,
                sourceStream,
                CancellationToken
            );
        }

        try
        {
            await using (var scope =
                _fixture.Services.CreateAsyncScope())
            {
                var processor = scope.ServiceProvider
                    .GetRequiredService<ImportJobProcessor>();

                var processed =
                    await processor.ProcessNextAsync(
                        CancellationToken
                    );

                Assert.True(
                    processed
                );
            }

            await using var verificationScope =
                _fixture.Services.CreateAsyncScope();

            var dbContext = verificationScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var storedDataset = await dbContext.Datasets
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == dataset.Id,
                    CancellationToken
                );

            var storedVersion = await dbContext.DatasetVersions
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == version.Id,
                    CancellationToken
                );

            var storedJob = await dbContext.ImportJobs
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == importJob.Id,
                    CancellationToken
                );

            Assert.Equal(
                version.Id,
                storedDataset.CurrentVersionId
            );

            Assert.Equal(
                DatasetVersionStatus.Ready,
                storedVersion.Status
            );

            Assert.Equal(
                resultStorageKey,
                storedVersion.StorageKey
            );

            Assert.Equal(
                3,
                storedVersion.RowCount
            );

            Assert.Equal(
                3,
                storedVersion.ColumnCount
            );

            Assert.Equal(
                ImportJobStatus.Completed,
                storedJob.Status
            );

            Assert.NotNull(
                storedJob.StartedAtUtc
            );

            Assert.NotNull(
                storedJob.CompletedAtUtc
            );

            Assert.Null(
                storedJob.ErrorMessage
            );

            var fileStorage = verificationScope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            var resultExists = await fileStorage.ExistsAsync(
                resultStorageKey,
                CancellationToken
            );

            Assert.True(
                resultExists
            );
        }
        finally
        {
            await using var scope =
                _fixture.Services.CreateAsyncScope();

            var fileStorage = scope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            await fileStorage.DeleteAsync(
                sourceStorageKey,
                CancellationToken
            );

            await fileStorage.DeleteAsync(
                resultStorageKey,
                CancellationToken
            );
        }
    }

    [Fact]
    public async Task ProcessNextAsync_ShouldFailJobAndVersion_WhenSourceFileDoesNotExist()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Failed import project"
        );

        var dataset = new Dataset(
            project.Id,
            "Broken dataset"
        );

        var version = new DatasetVersion(
            dataset.Id,
            1,
            "missing.csv"
        );

        var sourceStorageKey =
            $"tests/import-jobs/{Guid.NewGuid():N}/missing.csv";

        var resultStorageKey =
            $"datasets/{dataset.Id:N}/versions/{version.Id:N}/data.parquet";

        var importJob = new ImportJob(
            version.Id,
            "missing.csv",
            sourceStorageKey
        );

        await using (var scope =
            _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

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
            await using (var scope =
                _fixture.Services.CreateAsyncScope())
            {
                var processor = scope.ServiceProvider
                    .GetRequiredService<ImportJobProcessor>();

                var processed =
                    await processor.ProcessNextAsync(
                        CancellationToken
                    );

                Assert.True(
                    processed
                );
            }

            await using var verificationScope =
                _fixture.Services.CreateAsyncScope();

            var dbContext = verificationScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var storedDataset = await dbContext.Datasets
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == dataset.Id,
                    CancellationToken
                );

            var storedVersion = await dbContext.DatasetVersions
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == version.Id,
                    CancellationToken
                );

            var storedJob = await dbContext.ImportJobs
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == importJob.Id,
                    CancellationToken
                );

            Assert.Null(
                storedDataset.CurrentVersionId
            );

            Assert.Equal(
                DatasetVersionStatus.Failed,
                storedVersion.Status
            );

            Assert.Null(
                storedVersion.StorageKey
            );

            Assert.Null(
                storedVersion.RowCount
            );

            Assert.Null(
                storedVersion.ColumnCount
            );

            Assert.Equal(
                ImportJobStatus.Failed,
                storedJob.Status
            );

            Assert.NotNull(
                storedJob.StartedAtUtc
            );

            Assert.NotNull(
                storedJob.CompletedAtUtc
            );

            Assert.False(
                string.IsNullOrWhiteSpace(
                    storedJob.ErrorMessage
                )
            );

            var fileStorage = verificationScope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            var resultExists = await fileStorage.ExistsAsync(
                resultStorageKey,
                CancellationToken
            );

            Assert.False(
                resultExists
            );
        }
        finally
        {
            await using var scope =
                _fixture.Services.CreateAsyncScope();

            var fileStorage = scope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            await fileStorage.DeleteAsync(
                sourceStorageKey,
                CancellationToken
            );

            await fileStorage.DeleteAsync(
                resultStorageKey,
                CancellationToken
            );
        }
    }
}
