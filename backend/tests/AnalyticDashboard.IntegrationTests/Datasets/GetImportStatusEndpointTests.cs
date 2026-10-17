using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class GetImportStatusEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetImportStatusEndpointTests(
        ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    private static HttpRequestMessage CreateGetRequest(
        Guid projectId,
        Guid datasetId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{projectId}/datasets/{datasetId}/import-status"
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

    [Fact]
    public async Task GetImportStatus_ShouldReturnPendingForOwnedDataset()
    {
        var project = new Project(
            TestAuthHandler.User1Id,
            "Import worker project 1"
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

        var importJob = new ImportJob(
            version.Id,
            "sales.csv",
            sourceStorageKey
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
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

        using var request = CreateGetRequest(
            project.Id,
            dataset.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ImportStatusResponse>(
                JsonOptions,
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            importJob.Id,
            result.ImportJobId
        );

        Assert.Equal(
            version.Id,
            result.DatasetVersionId
        );

        Assert.Equal(
            ImportJobStatus.Pending,
            result.Status
        );

        Assert.Null(result.StartedAtUtc);

        Assert.Null(result.CompletedAtUtc);

        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnNotFoundForDifferentOwner()
    {
        var project = new Project(
            TestAuthHandler.User1Id,
            "Import worker project 2"
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

        var importJob = new ImportJob(
            version.Id,
            "sales.csv",
            sourceStorageKey
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
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

        using var request = CreateGetRequest(
            project.Id,
            dataset.Id,
            TestAuthHandler.User2Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnSafeErrorMessageForFailedImport()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Import worker project 2"
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

        var importJob = new ImportJob(
            version.Id,
            "sales.csv",
            sourceStorageKey
        );

        importJob.MarkRunning();
        importJob.MarkFailed(@"Internal error at C:\private\data.csv");

        version.MarkFailed();

        await using (var scope = _fixture.Services.CreateAsyncScope())
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

        using var request = CreateGetRequest(
            project.Id,
            dataset.Id,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ImportStatusResponse>(
                JsonOptions,
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            ImportJobStatus.Failed,
            result.Status
        );

        Assert.NotNull(result.StartedAtUtc);

        Assert.NotNull(result.CompletedAtUtc);

        Assert.Equal(
            "Import failed. Please try again.",
            result.ErrorMessage
        );
    }
}
