using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnalyticDashboard.Api.Contracts.Dashboards;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Widgets;

public sealed class GetWidgetDataEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetWidgetDataEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            method,
            url
        );

        if (userId.HasValue)
        {
            request.Headers.Add(
                TestAuthHandler.UserIdHeader,
                userId.Value.ToString()
            );
        }

        return request;
    }

    private static HttpRequestMessage CreateJsonRequest<T>(
        HttpMethod method,
        string url,
        T body,
        Guid? userId = null)
    {
        var request = CreateRequest(
            method,
            url,
            userId
        );

        request.Content = JsonContent.Create(body);

        return request;
    }

    private static HttpRequestMessage CreateImportRequest(
        Guid projectId,
        string fileName,
        string content,
        Guid? userId = null)
    {
        var request = CreateRequest(
            HttpMethod.Post,
            $"/projects/{projectId}/datasets/import/csv",
            userId
        );

        var multipart = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(
            Encoding.UTF8.GetBytes(content)
        );

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
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

    private static async Task<Guid> ReadIdAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                CancellationToken
            )
        );

        return document.RootElement
            .GetProperty("id")
            .GetGuid();
    }

    private async Task DeleteStorageObjectsAsync(
        params string?[] storageKeys)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var fileStorage = scope.ServiceProvider
            .GetRequiredService<IFileStorage>();

        foreach (var storageKey in storageKeys)
        {
            if (storageKey is null)
            {
                continue;
            }

            var exists = await fileStorage.ExistsAsync(
                storageKey,
                CancellationToken
            );

            if (!exists)
            {
                continue;
            }

            await fileStorage.DeleteAsync(
                storageKey,
                CancellationToken
            );
        }
    }

    [Fact]
    public async Task GetWidgetData_ShouldReturnAggregatedData_WhenWidgetConfigurationIsValid()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Widget data project"
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
                Country,Amount
                Poland,10
                Germany,20
                Poland,30
                """,
                userId
            );

            using var importResponse = await _fixture.Client.SendAsync(
                importRequest,
                CancellationToken
            );

            Assert.Equal(
                HttpStatusCode.Accepted,
                importResponse.StatusCode
            );

            using var importDocument = JsonDocument.Parse(
                await importResponse.Content
                    .ReadAsStringAsync(
                        CancellationToken
                    )
            );

            var importRoot = importDocument.RootElement;

            var datasetId = importRoot
                .GetProperty("datasetId")
                .GetGuid();

            var datasetVersionId = importRoot
                .GetProperty("datasetVersionId")
                .GetGuid();

            var importJobId = importRoot
                .GetProperty("importJobId")
                .GetGuid();

            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                var importJob = await dbContext.ImportJobs
                    .AsNoTracking()
                    .SingleAsync(
                        job =>
                            job.Id == importJobId,
                        CancellationToken
                    );

                sourceStorageKey = importJob.SourceStorageKey;
            }

            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var processor = scope.ServiceProvider
                    .GetRequiredService<ImportJobProcessor>();

                var processed = await processor.ProcessNextAsync(
                    CancellationToken
                );

                Assert.True(
                    processed
                );
            }

            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                var version = await dbContext.DatasetVersions
                    .AsNoTracking()
                    .SingleAsync(
                        entity =>
                            entity.Id == datasetVersionId,
                        CancellationToken
                    );

                Assert.Equal(
                    DatasetVersionStatus.Ready,
                    version.Status
                );

                Assert.NotNull(
                    version.StorageKey
                );

                resultStorageKey = version.StorageKey;
            }

            using var createDashboardRequest = CreateJsonRequest(
                HttpMethod.Post,
                $"/projects/{project.Id}/dashboards",
                new CreateDashboardRequest(
                    "Sales dashboard"
                ),
                userId
            );

            using var createDashboardResponse = await _fixture.Client.SendAsync(
                createDashboardRequest,
                CancellationToken
            );

            Assert.Equal(
                HttpStatusCode.Created,
                createDashboardResponse.StatusCode
            );

            var dashboardId = await ReadIdAsync(
                createDashboardResponse
            );

            var widgetType = Enum.GetValues<WidgetType>()
                .First();

            using var createWidgetRequest = CreateJsonRequest(
                HttpMethod.Post,
                $"/projects/{project.Id}/dashboards/{dashboardId}/widgets",
                new CreateWidgetRequest(
                    datasetId,
                    widgetType,
                    "Sales by country",
                    "Country",
                    "Amount",
                    "Sum"
                ),
                userId
            );

            using var createWidgetResponse = await _fixture.Client.SendAsync(
                createWidgetRequest,
                CancellationToken
            );

            Assert.Equal(
                HttpStatusCode.Created,
                createWidgetResponse.StatusCode
            );

            var widgetId = await ReadIdAsync(
                createWidgetResponse
            );

            using var getDataRequest = CreateRequest(
                HttpMethod.Get,
                $"/projects/{project.Id}/dashboards/{dashboardId}/widgets/{widgetId}/data",
                userId
            );

            using var getDataResponse = await _fixture.Client.SendAsync(
                getDataRequest,
                CancellationToken
            );

            Assert.Equal(
                HttpStatusCode.OK,
                getDataResponse.StatusCode
            );

            using var dataDocument = JsonDocument.Parse(
                await getDataResponse.Content
                    .ReadAsStringAsync(
                        CancellationToken
                    )
            );

            var root = dataDocument.RootElement;

            Assert.Equal(
                widgetId,
                root.GetProperty("widgetId").GetGuid()
            );

            Assert.Equal(
                "Sales by country",
                root.GetProperty("title").GetString()
            );

            var items = root.GetProperty("items").EnumerateArray().ToArray();

            Assert.Equal(
                2,
                items.Length
            );

            var poland = items.Single(item =>
                item.GetProperty("label").GetString() == "Poland"
            );

            var germany = items.Single(item =>
                item.GetProperty("label").GetString() == "Germany"
            );

            Assert.Equal(
                40d,
                poland.GetProperty("value").GetDouble()
            );

            Assert.Equal(
                20d,
                germany.GetProperty("value").GetDouble()
            );
        }
        finally
        {
            await DeleteStorageObjectsAsync(
                sourceStorageKey,
                resultStorageKey
            );
        }
    }
}
