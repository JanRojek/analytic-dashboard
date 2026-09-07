using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class GetDatasetByIdEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetDatasetByIdEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateGetRequest(
        Guid projectId,
        Guid datasetId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{projectId}/datasets/{datasetId}"
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

    private async Task AddDatasetAsync(
        Project project,
        Dataset dataset,
        DatasetVersion? version = null)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);
        dbContext.Datasets.Add(dataset);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );

        if (version is null)
        {
            return;
        }

        dbContext.DatasetVersions.Add(version);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );

        dataset.PublishVersion(
            version.Id
        );

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    [Fact]
    public async Task GetDatasetById_ShouldReturnOk_WhenDatasetBelongsToRequestedOwnedProject()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Dataset project"
        );

        var dataset = new Dataset(
            project.Id,
            "Sales"
        );

        var version = new DatasetVersion(
            dataset.Id,
            1,
            "sales.csv",
            $"datasets/{dataset.Id}/sales.csv"
        );

        version.MarkReady(
            25,
            4
        );

        await AddDatasetAsync(
            project,
            dataset,
            version
        );

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
            .ReadFromJsonAsync<DatasetResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            dataset.Id,
            result.Id
        );

        Assert.Equal(
            dataset.Name,
            result.Name
        );

        Assert.NotNull(
            result.CurrentVersion
        );

        Assert.Equal(
            version.Id,
            result.CurrentVersion.Id
        );

        Assert.Equal(
            version.VersionNumber,
            result.CurrentVersion.VersionNumber
        );

        Assert.Equal(
            version.OriginalFileName,
            result.CurrentVersion.OriginalFileName
        );

        Assert.Equal(
            25,
            result.CurrentVersion.RowCount
        );

        Assert.Equal(
            4,
            result.CurrentVersion.ColumnCount
        );
    }

    [Fact]
    public async Task GetDatasetById_ShouldReturnNotFound_WhenDatasetBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 dataset project"
        );

        var dataset = new Dataset(
            project.Id,
            "Private dataset"
        );

        await AddDatasetAsync(
            project,
            dataset
        );

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
            HttpStatusCode.NotFound,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetDatasetById_ShouldReturnNotFound_WhenDatasetBelongsToDifferentProject()
    {
        var userId = Guid.NewGuid();

        var actualProject = new Project(
            userId,
            "Actual project"
        );

        var requestedProject = new Project(
            userId,
            "Requested project"
        );

        var dataset = new Dataset(
            actualProject.Id,
            "Dataset"
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            dbContext.Projects.AddRange(
                actualProject,
                requestedProject
            );

            await dbContext.SaveChangesAsync(
                CancellationToken
            );

            dbContext.Datasets.Add(dataset);

            await dbContext.SaveChangesAsync(
                CancellationToken
            );
        }

        using var request = CreateGetRequest(
            requestedProject.Id,
            dataset.Id,
            userId
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
}
