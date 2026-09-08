using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Analytics;

public sealed class QueryDatasetEndpointTests
    : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public QueryDatasetEndpointTests(
        ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueryDataset_ShouldGroupAndSumNumericColumn()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Analytics project"
        );

        await AddProjectAsync(
            project
        );

        string? sourceStorageKey = null;
        string? resultStorageKey = null;

        try
        {
            using var importRequest = CreateImportRequest(
                project.Id,
                "sales.csv",
                """
                Name,Amount,Country
                Alice,10,Poland
                Bob,20,Germany
                Charlie,30,Poland
                """,
                userId
            );

            using var importResponse =
                await _fixture.Client.SendAsync(
                    importRequest,
                    CancellationToken
                );

            Assert.Equal(
                HttpStatusCode.Accepted,
                importResponse.StatusCode
            );

            var importResult = await importResponse.Content
                .ReadFromJsonAsync<ImportCsvDatasetResponse>(
                    CancellationToken
                );

            Assert.NotNull(
                importResult
            );

            await using (var scope =
                _fixture.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                var importJob = await dbContext.ImportJobs
                    .AsNoTracking()
                    .SingleAsync(
                        job =>
                            job.Id == importResult.ImportJobId,
                        CancellationToken
                    );

                sourceStorageKey =
                    importJob.SourceStorageKey;
            }

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

            await using (var scope =
                _fixture.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                var version = await dbContext.DatasetVersions
                    .AsNoTracking()
                    .SingleAsync(
                        entity =>
                            entity.Id
                            == importResult.DatasetVersionId,
                        CancellationToken
                    );

                Assert.Equal(
                    DatasetVersionStatus.Ready,
                    version.Status
                );

                resultStorageKey =
                    version.StorageKey;

                Assert.NotNull(
                    resultStorageKey
                );
            }

            using var queryRequest = CreateQueryRequest(
                project.Id,
                importResult.DatasetId,
                userId
            );

            using var queryResponse =
                await _fixture.Client.SendAsync(
                    queryRequest,
                    CancellationToken
                );

            Assert.Equal(
                HttpStatusCode.OK,
                queryResponse.StatusCode
            );

            var result = await queryResponse.Content
                .ReadFromJsonAsync<QueryDatasetResponse>(
                    CancellationToken
                );

            Assert.NotNull(
                result
            );

            Assert.Equal(
                2,
                result.Items.Count
            );

            var poland = result.Items.Single(
                item => item.Label == "Poland"
            );

            var germany = result.Items.Single(
                item => item.Label == "Germany"
            );

            Assert.Equal(
                40d,
                poland.Value
            );

            Assert.Equal(
                20d,
                germany.Value
            );
        }
        finally
        {
            await using var scope =
                _fixture.Services.CreateAsyncScope();

            var fileStorage = scope.ServiceProvider
                .GetRequiredService<IFileStorage>();

            if (sourceStorageKey is not null)
            {
                await fileStorage.DeleteAsync(
                    sourceStorageKey,
                    CancellationToken
                );
            }

            if (resultStorageKey is not null)
            {
                await fileStorage.DeleteAsync(
                    resultStorageKey,
                    CancellationToken
                );
            }
        }
    }

    private static HttpRequestMessage CreateImportRequest(
        Guid projectId,
        string fileName,
        string content,
        Guid userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{projectId}/datasets/import/csv"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            userId.ToString()
        );

        var multipart =
            new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(
            Encoding.UTF8.GetBytes(content)
        );

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                "text/csv"
            );

        multipart.Add(
            fileContent,
            "file",
            fileName
        );

        request.Content = multipart;

        return request;
    }

    private static HttpRequestMessage CreateQueryRequest(
        Guid projectId,
        Guid datasetId,
        Guid userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{projectId}/datasets/{datasetId}/query"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            userId.ToString()
        );

        request.Content = JsonContent.Create(
            new QueryDatasetRequest(
                "Country",
                "Amount",
                "Sum"
            )
        );

        return request;
    }

    private async Task AddProjectAsync(
        Project project)
    {
        await using var scope =
            _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(
            project
        );

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }
}
