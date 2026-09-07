using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnalyticDashboard.Application.Storage;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class ImportCsvDatasetEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ImportCsvDatasetEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateImportRequest(
        Guid projectId,
        string fileName,
        string content,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{projectId}/datasets/import/csv"
        );

        if (userId.HasValue)
        {
            request.Headers.Add(
                TestAuthHandler.UserIdHeader,
                userId.Value.ToString()
            );
        }

        var multipart = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(
            Encoding.UTF8.GetBytes(content)
        );

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("text/csv");

        multipart.Add(
            fileContent,
            "file",
            fileName
        );

        request.Content = multipart;

        return request;
    }

    private async Task AddProjectAsync(
        Project project)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    [Fact]
    public async Task ImportCsvDataset_ShouldCreateDatasetForOwnedProject()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Import dataset project"
        );

        await AddProjectAsync(project);

        string? storageKey = null;

        try
        {
            using var request = CreateImportRequest(
                project.Id,
                "sales.csv",
                "Name,Amount\nA,10\nB,20\n",
                userId
            );

            using var response = await _fixture.Client.SendAsync(
                request,
                CancellationToken
            );

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode
            );

            var result = await response.Content
                .ReadFromJsonAsync<ImportCsvDatasetResponse>(
                    CancellationToken
                );

            Assert.NotNull(result);

            await using var scope =
                _fixture.Services.CreateAsyncScope();

            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var dataset = await dbContext.Datasets
                .AsNoTracking()
                .SingleAsync(
                    entity => entity.Id == result.Id,
                    CancellationToken
                );

            Assert.Equal(
                project.Id,
                dataset.ProjectId
            );

            Assert.Equal(
                "sales",
                dataset.Name
            );

            Assert.NotNull(
                dataset.CurrentVersionId
            );

            var version = await dbContext.DatasetVersions
                .AsNoTracking()
                .SingleAsync(
                    entity =>
                        entity.Id == dataset.CurrentVersionId.Value,
                    CancellationToken
                );

            storageKey = version.StorageKey;

            Assert.Equal(
                dataset.Id,
                version.DatasetId
            );

            Assert.Equal(
                1,
                version.VersionNumber
            );

            Assert.Equal(
                "sales.csv",
                version.OriginalFileName
            );

            Assert.Equal(
                DatasetVersionStatus.Ready,
                version.Status
            );

            Assert.Equal(
                2,
                version.RowCount
            );

            Assert.Equal(
                2,
                version.ColumnCount
            );

            var fileStorage = scope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            var fileExists = await fileStorage.ExistsAsync(
                version.StorageKey,
                CancellationToken
            );

            Assert.True(
                fileExists
            );
        }
        finally
        {
            if (storageKey is not null)
            {
                await using var scope =
                    _fixture.Services.CreateAsyncScope();

                var fileStorage = scope.ServiceProvider
                    .GetRequiredService<IFileStorage>();

                await fileStorage.DeleteAsync(
                    storageKey,
                    CancellationToken
                );
            }
        }
    }

    [Fact]
    public async Task ImportCsvDataset_ShouldReturnNotFoundBeforeProcessingFile_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 import project"
        );

        await AddProjectAsync(project);

        using var request = CreateImportRequest(
            project.Id,
            "definitely-not-csv.txt",
            "This file should never be processed.",
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var datasetExists = await dbContext.Datasets.AnyAsync(
            dataset => dataset.ProjectId == project.Id,
            CancellationToken
        );

        Assert.False(datasetExists);
    }
}
